using System.IO;
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

    /// <summary>在 CurseForge 上匹配到的文件数。</summary>
    public int Matched { get; init; }

    public int Unmatched => Math.Max(Checked - Matched, 0);
}

/// <summary>
/// 已安装资源的更新检查。
/// <para>
/// 用 CurseForge 指纹接口把本地 jar / zip 对应到项目，再查询项目在该游戏版本下的文件，
/// 只有找到发布时间不早于当前文件的新版本时才标记为可更新。
/// </para>
/// </summary>
public sealed class ResourceUpdateService
{
    private const int FingerprintBatchSize = 100;

    private readonly CurseForgeService _curseForge;
    private readonly ILogger _log;

    public ResourceUpdateService(CurseForgeService curseForge, ILogger log)
    {
        _curseForge = curseForge;
        _log = log;
    }

    public async Task<ResourceUpdateCheckResult> CheckAsync(
        IReadOnlyList<LocalResource> resources,
        string? gameVersion,
        string? loaderName,
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

        if (fingerprintMap.Count == 0)
        {
            return new ResourceUpdateCheckResult { Checked = files.Count };
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

        var updates = new List<ResourceUpdateInfo>();

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();

            if (!fingerprintMap.TryGetValue(match.Fingerprint, out var resource))
            {
                continue;
            }

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

            updates.Add(new ResourceUpdateInfo
            {
                Resource = resource,
                File = latest,
                CurrentVersionText = resource.Version ?? match.File.DisplayName,
            });
        }

        _log.Info($"检查更新完成：{files.Count} 个文件，匹配 {matches.Count} 个，可更新 {updates.Count} 个");
        return new ResourceUpdateCheckResult
        {
            Updates = updates,
            Checked = files.Count,
            Matched = matches.Count,
        };
    }
}
