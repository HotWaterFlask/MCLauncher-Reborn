using System.IO;
using System.Security.Cryptography;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>一次检查更新的汇总。</summary>
public sealed class ResourceUpdateCheckResult
{
    public IReadOnlyList<ResourceUpdateInfo> Updates { get; init; } = Array.Empty<ResourceUpdateInfo>();

    /// <summary>参与检查的文件数。</summary>
    public int Checked { get; init; }

    /// <summary>在资源站上匹配到的文件数。</summary>
    public int Matched { get; init; }

    public int Unmatched => Math.Max(Checked - Matched, 0);
}

/// <summary>
/// 已安装资源的更新检查。
/// <para>
/// 先用设置的「附加内容默认下载源」匹配本地文件，没匹配到的再换另一个源：
/// Modrinth 用 SHA-1 反查版本，CurseForge 用 MurmurHash2 指纹。
/// 匹配到项目后只取当前游戏版本 / 加载器下发布时间更新的文件。
/// </para>
/// </summary>
public sealed class ResourceUpdateService
{
    private const int FingerprintBatchSize = 100;

    private readonly CurseForgeService _curseForge;
    private readonly ModrinthService _modrinth;
    private readonly ILogger _log;

    public ResourceUpdateService(CurseForgeService curseForge, ModrinthService modrinth, ILogger log)
    {
        _curseForge = curseForge;
        _modrinth = modrinth;
        _log = log;
    }

    public async Task<ResourceUpdateCheckResult> CheckAsync(
        IReadOnlyList<LocalResource> resources,
        string? gameVersion,
        string? loaderName,
        ResourceSource preferredSource,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var files = resources
            .Where(resource => resource.Kind is ResourceKind.Mod or ResourceKind.ResourcePack or ResourceKind.ShaderPack)
            .Where(resource => File.Exists(resource.FilePath))
            .ToList();

        if (files.Count == 0)
        {
            return new ResourceUpdateCheckResult();
        }

        var updates = new List<ResourceUpdateInfo>();
        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sources = preferredSource == ResourceSource.Modrinth
            ? new[] { ResourceSource.Modrinth, ResourceSource.CurseForge }
            : new[] { ResourceSource.CurseForge, ResourceSource.Modrinth };

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();

            var pending = files.Where(file => !matchedPaths.Contains(file.FilePath)).ToList();
            if (pending.Count == 0)
            {
                break;
            }

            // CurseForge 没配置 API Key 时直接跳过，交给另一个源。
            if (source == ResourceSource.CurseForge && !_curseForge.IsConfigured)
            {
                _log.Info("未配置 CurseForge API Key，跳过 CurseForge 更新检查");
                continue;
            }

            IReadOnlyDictionary<string, ResourceUpdateInfo?> found;
            try
            {
                found = source == ResourceSource.Modrinth
                    ? await CheckModrinthAsync(pending, gameVersion, loaderName, status, ct).ConfigureAwait(false)
                    : await CheckCurseForgeAsync(pending, gameVersion, loaderName, status, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"{DisplayName(source)} 检查更新失败", ex);
                status?.Report($"{DisplayName(source)} 检查失败：" + ex.Message);
                continue;
            }

            foreach (var (path, update) in found)
            {
                matchedPaths.Add(path);
                if (update is not null)
                {
                    updates.Add(update);
                }
            }
        }

        _log.Info(
            $"检查更新完成：{files.Count} 个文件，匹配 {matchedPaths.Count} 个，"
            + $"可更新 {updates.Count} 个（优先 {DisplayName(preferredSource)}）");

        return new ResourceUpdateCheckResult
        {
            Updates = updates,
            Checked = files.Count,
            Matched = matchedPaths.Count,
        };
    }

    // ---------------------------------------------------------------- Modrinth

    /// <returns>键为本地文件路径；键存在表示匹配到了项目，值为 null 表示没有可更新版本。</returns>
    private async Task<IReadOnlyDictionary<string, ResourceUpdateInfo?>> CheckModrinthAsync(
        IReadOnlyList<LocalResource> files,
        string? gameVersion,
        string? loaderName,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var hashMap = new Dictionary<string, LocalResource>(StringComparer.OrdinalIgnoreCase);
        status?.Report("正在计算本地文件 SHA-1…");

        foreach (var resource in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var sha1 = await Task.Run(() => ComputeSha1(resource.FilePath), ct).ConfigureAwait(false);
                hashMap.TryAdd(sha1, resource);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"计算文件 SHA-1 失败：{resource.FileName}", ex);
            }
        }

        var result = new Dictionary<string, ResourceUpdateInfo?>(StringComparer.OrdinalIgnoreCase);
        if (hashMap.Count == 0)
        {
            return result;
        }

        status?.Report($"正在查询 Modrinth（{hashMap.Count} 个文件）…");
        var matches = await _modrinth.MatchHashesAsync(hashMap.Keys.ToList(), ct).ConfigureAwait(false);

        foreach (var (hash, current) in matches)
        {
            ct.ThrowIfCancellationRequested();

            if (!hashMap.TryGetValue(hash, out var resource))
            {
                continue;
            }

            result[resource.FilePath] = null;

            if (string.IsNullOrWhiteSpace(current.ProjectId))
            {
                continue;
            }

            IReadOnlyList<ModrinthVersion> versions;
            try
            {
                versions = await _modrinth
                    .GetVersionsAsync(current.ProjectId, resource.Kind, gameVersion, loaderName, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"查询 Modrinth 项目版本失败：{current.ProjectId}", ex);
                continue;
            }

            var latest = ModrinthService.PickBestVersion(versions, resource.Kind, loaderName);
            if (latest is null
                || latest.Id == current.Id
                || latest.PublishedAt <= current.PublishedAt)
            {
                continue;
            }

            var file = ModrinthService.ToResourceFile(latest, resource.Kind);
            if (file is null || string.IsNullOrWhiteSpace(file.DownloadUrl))
            {
                continue;
            }

            result[resource.FilePath] = new ResourceUpdateInfo
            {
                Resource = resource,
                Source = ResourceSource.Modrinth,
                File = file,
                CurrentVersionText = resource.Version ?? CurrentVersionText(current),
            };
        }

        return result;
    }

    private static string CurrentVersionText(ModrinthVersion version) =>
        string.IsNullOrWhiteSpace(version.Name) ? version.VersionNumber : version.Name;

    private static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- CurseForge

    private async Task<IReadOnlyDictionary<string, ResourceUpdateInfo?>> CheckCurseForgeAsync(
        IReadOnlyList<LocalResource> files,
        string? gameVersion,
        string? loaderName,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var fingerprintMap = new Dictionary<uint, LocalResource>();
        status?.Report("正在计算本地文件指纹…");

        foreach (var resource in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fingerprint = await Task.Run(
                        () => MurmurHash2.ComputeFileFingerprint(resource.FilePath),
                        ct)
                    .ConfigureAwait(false);

                // 同一个文件不会出现两次；极小概率碰撞时保留第一个。
                fingerprintMap.TryAdd(fingerprint, resource);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"计算文件指纹失败：{resource.FileName}", ex);
            }
        }

        var result = new Dictionary<string, ResourceUpdateInfo?>(StringComparer.OrdinalIgnoreCase);
        if (fingerprintMap.Count == 0)
        {
            return result;
        }

        status?.Report($"正在查询 CurseForge（{fingerprintMap.Count} 个文件）…");

        var matches = new List<CurseForgeFingerprintMatch>();
        var fingerprints = fingerprintMap.Keys.ToList();

        for (var offset = 0; offset < fingerprints.Count; offset += FingerprintBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = fingerprints.Skip(offset).Take(FingerprintBatchSize).ToList();
            matches.AddRange(await _curseForge.MatchFingerprintsAsync(batch, ct).ConfigureAwait(false));
        }

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();

            if (!fingerprintMap.TryGetValue(match.Fingerprint, out var resource))
            {
                continue;
            }

            result[resource.FilePath] = null;

            IReadOnlyList<CurseForgeFile> filesForVersion;
            try
            {
                filesForVersion = await _curseForge
                    .GetFilesAsync(match.ProjectId, gameVersion, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                _log.Warn($"查询 CurseForge 项目文件失败：{match.ProjectId}", ex);
                continue;
            }

            var latest = CurseForgeService.PickBestFile(filesForVersion, resource.Kind, loaderName)
                         ?? CurseForgeService.PickBestFile(match.LatestFiles, resource.Kind, loaderName);

            if (latest is null
                || latest.Id == match.File.Id
                || latest.FileDate < match.File.FileDate
                || string.IsNullOrWhiteSpace(latest.DownloadUrl))
            {
                continue;
            }

            result[resource.FilePath] = new ResourceUpdateInfo
            {
                Resource = resource,
                Source = ResourceSource.CurseForge,
                File = ResourceFile.FromCurseForge(latest),
                CurrentVersionText = resource.Version ?? match.File.DisplayName,
            };
        }

        return result;
    }

    private static string DisplayName(ResourceSource source) =>
        source == ResourceSource.Modrinth ? "Modrinth" : "CurseForge";
}
