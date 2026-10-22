using System.Diagnostics;
using System.Text.RegularExpressions;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>支持的加载器种类。</summary>
public enum ModLoaderKind
{
    Fabric,
    Quilt,
    Forge,
    NeoForge,
    OptiFine,
}

/// <summary>加载器版本选项。</summary>
public sealed class LoaderVersionOption
{
    public required string Version { get; init; }

    public bool IsRecommended { get; init; }

    public bool IsLatest { get; init; }

    public string Display => IsRecommended
        ? $"{Version}（推荐）"
        : IsLatest ? $"{Version}（最新）" : Version;

    public override string ToString() => Display;
}

/// <summary>加载器安装请求。</summary>
public sealed class LoaderInstallRequest
{
    public required string GameRoot { get; init; }

    /// <summary>基底原版版本 id（必须已经安装）。</summary>
    public required string GameVersion { get; init; }

    public required ModLoaderKind Kind { get; init; }

    public required string LoaderVersion { get; init; }

    public InstallOptions InstallOptions { get; init; } = new();
}

/// <summary>加载器安装结果。</summary>
public sealed class LoaderInstallResult
{
    public bool Success { get; init; }

    /// <summary>安装后产生的版本 id（用于界面自动选中）。</summary>
    public string? VersionId { get; init; }

    public string? Error { get; init; }

    public static LoaderInstallResult Ok(string versionId) => new() { Success = true, VersionId = versionId };

    public static LoaderInstallResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// 加载器自动安装。
/// <list type="bullet">
/// <item><b>Fabric / Quilt</b>：拿 <c>profile/json</c> 直接落一个 <c>inheritsFrom</c> 的版本 JSON，**全程不需要 Java**。</item>
/// <item><b>Forge / NeoForge</b>：下载 installer jar 并执行 <c>--installClient</c>（需要 <c>launcher_profiles.json</c> 已存在）。</item>
/// <item><b>OptiFine</b>：第一版只做"把 jar 放进 mods"，不做版本 JSON 注入。</item>
/// </list>
/// </summary>
public sealed partial class ModLoaderService
{
    private const string FabricMetaBase = "https://meta.fabricmc.net/v2";
    private const string QuiltMetaBase = "https://meta.quiltmc.org/v3";

    private const string ForgeMavenBase = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private const string ForgeMetadataUrl = ForgeMavenBase + "/maven-metadata.xml";
    private const string ForgePromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

    private const string NeoForgeBase = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private const string NeoForgeMetadataUrl = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";

    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpDownloader _downloader;
    private readonly DownloadSourceResolver _resolver;
    private readonly VersionRepository _repository;
    private readonly GameInstaller _installer;
    private readonly JavaService _javaService;
    private readonly ILogger _log;

    public ModLoaderService(
        HttpDownloader downloader,
        DownloadSourceResolver resolver,
        VersionRepository repository,
        GameInstaller installer,
        JavaService javaService,
        ILogger log)
    {
        _downloader = downloader;
        _resolver = resolver;
        _repository = repository;
        _installer = installer;
        _javaService = javaService;
        _log = log;
    }

    [GeneratedRegex(@"<version>([^<]+)</version>")]
    private static partial Regex MavenVersionPattern();

    public static string DisplayName(ModLoaderKind kind) => kind switch
    {
        ModLoaderKind.Fabric => "Fabric",
        ModLoaderKind.Quilt => "Quilt",
        ModLoaderKind.Forge => "Forge",
        ModLoaderKind.NeoForge => "NeoForge",
        _ => "OptiFine",
    };

    /// <summary>
    /// 解析整合包里的加载器描述，兼容 <c>Fabric 0.15.3</c> 与 CurseForge 的 <c>fabric-0.15.3</c>。
    /// </summary>
    public static (ModLoaderKind Kind, string? Version)? ParseLoaderDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = description.Trim();
        var lower = text.ToLowerInvariant();

        var kind = lower switch
        {
            _ when lower.StartsWith("fabric", StringComparison.Ordinal) => ModLoaderKind.Fabric,
            _ when lower.StartsWith("quilt", StringComparison.Ordinal) => ModLoaderKind.Quilt,
            _ when lower.StartsWith("neoforge", StringComparison.Ordinal) => ModLoaderKind.NeoForge,
            _ when lower.StartsWith("forge", StringComparison.Ordinal) => ModLoaderKind.Forge,
            _ => (ModLoaderKind?)null,
        };

        if (kind is null)
        {
            return null;
        }

        var separator = text.IndexOfAny(new[] { ' ', '-', ':' });
        var version = separator >= 0 ? text[(separator + 1)..].Trim() : null;
        return (kind.Value, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    // ------------------------------------------------------------------ 版本列表

    /// <summary>列出该加载器针对某个原版版本的可用加载器版本。</summary>
    public async Task<IReadOnlyList<LoaderVersionOption>> ListLoaderVersionsAsync(
        ModLoaderKind kind,
        string gameVersion,
        CancellationToken ct = default)
    {
        return kind switch
        {
            ModLoaderKind.Fabric => await ListFabricLikeAsync(FabricMetaBase, ct).ConfigureAwait(false),
            ModLoaderKind.Quilt => await ListFabricLikeAsync(QuiltMetaBase, ct).ConfigureAwait(false),
            ModLoaderKind.Forge => await ListForgeAsync(gameVersion, ct).ConfigureAwait(false),
            ModLoaderKind.NeoForge => await ListNeoForgeAsync(gameVersion, ct).ConfigureAwait(false),
            _ => Array.Empty<LoaderVersionOption>(),
        };
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> ListFabricLikeAsync(string metaBase, CancellationToken ct)
    {
        var json = await _downloader
            .RequestStringAsync(metaBase + "/versions/loader", HttpMethod.Get, null, null, null, ct)
            .ConfigureAwait(false);

        var items = JsonUtil.Deserialize<List<FabricLoaderEntry>>(json) ?? new List<FabricLoaderEntry>();

        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.Version))
            .Select(i => new LoaderVersionOption
            {
                Version = i.Version!,
                IsLatest = items.FirstOrDefault()?.Version == i.Version,
            })
            .Take(80)
            .ToList();
    }

    /// <summary>该原版版本是否有对应的 Fabric/Quilt 加载器（meta 里能查到 profile 即可）。</summary>
    public async Task<bool> HasLoaderForAsync(ModLoaderKind kind, string gameVersion, CancellationToken ct = default)
    {
        var baseUrl = kind == ModLoaderKind.Quilt ? QuiltMetaBase : FabricMetaBase;
        try
        {
            var json = await _downloader
                .RequestStringAsync(baseUrl + "/versions/game", HttpMethod.Get, null, null, null, ct)
                .ConfigureAwait(false);

            var games = JsonUtil.Deserialize<List<FabricGameEntry>>(json) ?? new List<FabricGameEntry>();
            return games.Any(g => string.Equals(g.Version, gameVersion, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _log.Warn($"查询 {DisplayName(kind)} 支持的原版版本失败", ex);
            return false;
        }
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> ListForgeAsync(string gameVersion, CancellationToken ct)
    {
        var xml = await _downloader
            .RequestStringAsync(ForgeMetadataUrl, HttpMethod.Get, null, null, null, ct)
            .ConfigureAwait(false);

        var recommended = string.Empty;
        var latest = string.Empty;

        try
        {
            var promos = JsonUtil.Deserialize<ForgePromotions>(
                await _downloader.RequestStringAsync(ForgePromotionsUrl, HttpMethod.Get, null, null, null, ct).ConfigureAwait(false));

            recommended = promos?.Promos?.GetValueOrDefault($"{gameVersion}-recommended") ?? string.Empty;
            latest = promos?.Promos?.GetValueOrDefault($"{gameVersion}-latest") ?? string.Empty;
        }
        catch (Exception ex)
        {
            _log.Warn("获取 Forge 推荐版本失败（不影响列表）", ex);
        }

        var prefix = gameVersion + "-";
        var result = new List<LoaderVersionOption>();

        foreach (Match match in MavenVersionPattern().Matches(xml))
        {
            var full = match.Groups[1].Value.Trim();
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = full[prefix.Length..];
            result.Add(new LoaderVersionOption
            {
                Version = version,
                IsRecommended = version == recommended,
                IsLatest = version == latest,
            });
        }

        result.Reverse();
        return result;
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> ListNeoForgeAsync(string gameVersion, CancellationToken ct)
    {
        var json = await _downloader
            .RequestStringAsync(NeoForgeMetadataUrl, HttpMethod.Get, null, null, null, ct)
            .ConfigureAwait(false);

        var payload = JsonUtil.Deserialize<MavenVersionsPayload>(json) ?? new MavenVersionsPayload();
        var prefix = McVersionUtil.GetBaseVersion(gameVersion) switch
        {
            var v when v.StartsWith("1.", StringComparison.Ordinal) => v[2..] + ".",
            var v => v + ".",
        };

        var result = payload.Versions
            .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(v => new LoaderVersionOption { Version = v, IsLatest = v == payload.Versions.LastOrDefault() })
            .ToList();

        result.Reverse();
        return result;
    }

    // ------------------------------------------------------------------ 安装

    public async Task<LoaderInstallResult> InstallAsync(
        LoaderInstallRequest request,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_repository.IsInstalled(request.GameRoot, request.GameVersion))
        {
            return LoaderInstallResult.Fail($"基底版本 {request.GameVersion} 还没有安装，请先安装它");
        }

        try
        {
            return request.Kind switch
            {
                ModLoaderKind.Fabric or ModLoaderKind.Quilt => await InstallFabricLikeAsync(request, progress, ct).ConfigureAwait(false),
                ModLoaderKind.Forge or ModLoaderKind.NeoForge => await InstallViaInstallerJarAsync(request, progress, ct).ConfigureAwait(false),
                _ => LoaderInstallResult.Fail("OptiFine 请使用「作为 Mod 安装」的方式，不要走加载器安装"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"安装 {DisplayName(request.Kind)} {request.LoaderVersion} 失败", ex);
            return LoaderInstallResult.Fail(ex.Message);
        }
    }

    private async Task<LoaderInstallResult> InstallFabricLikeAsync(
        LoaderInstallRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        var metaBase = request.Kind == ModLoaderKind.Quilt ? QuiltMetaBase : FabricMetaBase;

        progress?.Report(new InstallProgress
        {
            Stage = $"获取 {DisplayName(request.Kind)} 配置",
            Completed = 0,
            Total = 1,
        });

        var url = $"{metaBase}/versions/loader/{Uri.EscapeDataString(request.GameVersion)}/"
                  + $"{Uri.EscapeDataString(request.LoaderVersion)}/profile/json";

        var json = await _downloader
            .RequestStringAsync(url, HttpMethod.Get, null, null, null, ct)
            .ConfigureAwait(false);

        var version = JsonUtil.Deserialize<GameVersionJson>(json)
                      ?? throw new InvalidDataException($"{DisplayName(request.Kind)} 没有返回可用的版本 JSON");

        if (string.IsNullOrWhiteSpace(version.Id))
        {
            throw new InvalidDataException("profile JSON 缺少 id");
        }

        if (!string.Equals(version.InheritsFrom, request.GameVersion, StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn($"profile JSON 的 inheritsFrom（{version.InheritsFrom}）与请求的基底（{request.GameVersion}）不一致");
        }

        _repository.Save(request.GameRoot, version);
        _log.Info($"已写入 {DisplayName(request.Kind)} 版本 JSON：{version.Id}");

        var report = await _installer
            .EnsureCompleteAsync(request.GameRoot, version.Id!, request.InstallOptions, progress, ct)
            .ConfigureAwait(false);

        return report.Success
            ? LoaderInstallResult.Ok(version.Id!)
            : LoaderInstallResult.Fail($"加载器已写入，但依赖文件下载失败（{report.Failed} 个）");
    }

    private async Task<LoaderInstallResult> InstallViaInstallerJarAsync(
        LoaderInstallRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        var kind = request.Kind;
        var expectedVersionId = kind == ModLoaderKind.Forge
            ? $"{request.GameVersion}-forge-{request.LoaderVersion}"
            : $"neoforge-{request.LoaderVersion}";

        // Forge / NeoForge 的安装器都要读 launcher_profiles.json，没有就直接失败。
        _installer.CreateLauncherProfiles(request.GameRoot);

        var installerUrl = kind == ModLoaderKind.Forge
            ? $"{ForgeMavenBase}/{Uri.EscapeDataString($"{request.GameVersion}-{request.LoaderVersion}")}/"
              + $"forge-{Uri.EscapeDataString($"{request.GameVersion}-{request.LoaderVersion}")}-installer.jar"
            : $"{NeoForgeBase}/{Uri.EscapeDataString(request.LoaderVersion)}/"
              + $"neoforge-{Uri.EscapeDataString(request.LoaderVersion)}-installer.jar";

        var tempDirectory = Path.Combine(request.GameRoot, ".mclauncher", "temp");
        PathUtil.EnsureDirectory(tempDirectory);
        var installerPath = Path.Combine(tempDirectory, $"{DisplayName(kind)}-{request.LoaderVersion}-installer.jar");

        progress?.Report(new InstallProgress
        {
            Stage = $"下载 {DisplayName(kind)} 安装器",
            Completed = 0,
            Total = 1,
        });

        var outcome = await _downloader.DownloadAsync(
            _resolver.Resolve(installerUrl, DownloadUrlKind.ModLoaderInstaller),
            installerPath,
            FileChecker.ExistenceOnly,
            null,
            ct).ConfigureAwait(false);

        if (!outcome.Success)
        {
            return LoaderInstallResult.Fail($"下载安装器失败：{outcome.Error}");
        }

        var java = _javaService.SelectBest(RequiredJavaMajor(request.GameVersion));
        if (java is null)
        {
            return LoaderInstallResult.Fail("没有可用的 64 位 Java，无法运行安装器");
        }

        progress?.Report(new InstallProgress
        {
            Stage = $"运行 {DisplayName(kind)} 安装器",
            Completed = 0,
            Total = 1,
            Current = installerPath,
        });

        var (exitCode, output) = await RunInstallerAsync(java.Path, installerPath, request.GameRoot, ct).ConfigureAwait(false);

        if (exitCode != 0)
        {
            _log.Error($"{DisplayName(kind)} 安装器退出码 {exitCode}");
            return LoaderInstallResult.Fail($"安装器执行失败（退出码 {exitCode}）：{Tail(output)}");
        }

        if (!_repository.IsInstalled(request.GameRoot, expectedVersionId))
        {
            return LoaderInstallResult.Fail($"安装器执行成功，但没有生成期望的版本 {expectedVersionId}。安装器输出：{Tail(output)}");
        }

        // 安装器只保证版本 JSON 就位，库与 natives 由我们再校验补全一遍。
        var report = await _installer
            .EnsureCompleteAsync(request.GameRoot, expectedVersionId, request.InstallOptions, progress, ct)
            .ConfigureAwait(false);

        return report.Success
            ? LoaderInstallResult.Ok(expectedVersionId)
            : LoaderInstallResult.Fail($"加载器已安装，但补全依赖失败（{report.Failed} 个）");
    }

    private static async Task<(int ExitCode, string Output)> RunInstallerAsync(
        string javaPath,
        string installerPath,
        string gameRoot,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(javaPath)
        {
            WorkingDirectory = gameRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // 用 ArgumentList 传参，路径里有空格也不会被切错。
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("--installClient");
        startInfo.ArgumentList.Add(gameRoot);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动 Java 安装器进程");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(InstallerTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 已经退出就忽略。
            }

            throw new TimeoutException($"安装器超过 {InstallerTimeout.TotalMinutes:0} 分钟没有结束");
        }

        var output = string.Empty;
        try
        {
            output = await stdoutTask.ConfigureAwait(false) + Environment.NewLine + await stderrTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 输出读取失败不影响退出码判定。
        }

        return (process.ExitCode, output);
    }

    /// <summary>安装器需要与目标版本匹配的 Java，这里按原版版本号推断主版本。</summary>
    public static int RequiredJavaMajor(string gameVersion)
    {
        if (McVersionUtil.IsAtLeast(gameVersion, 1, 20, 5))
        {
            return 21;
        }

        if (McVersionUtil.IsAtLeast(gameVersion, 1, 18, 0))
        {
            return 17;
        }

        if (McVersionUtil.IsAtLeast(gameVersion, 1, 17, 0))
        {
            return 16;
        }

        return 8;
    }

    private static string Tail(string text, int lines = 12)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "（安装器没有输出）";
        }

        var all = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" / ", all.TakeLast(lines).Select(l => l.Trim()));
    }

    // ------------------------------------------------------------------ OptiFine

    /// <summary>
    /// 把用户提供的 OptiFine jar 作为 Mod 放进 <c>mods/</c>。
    /// 第一版不做版本 JSON 注入：只适用于"OptiFine 以 Mod 形式加载"的版本。
    /// </summary>
    public void InstallOptifineAsMod(string gameRoot, string modsDirectory, string optifineJarPath)
    {
        if (!File.Exists(optifineJarPath))
        {
            throw new FileNotFoundException("没有找到 OptiFine jar", optifineJarPath);
        }

        PathUtil.EnsureDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, Path.GetFileName(optifineJarPath));
        File.Copy(optifineJarPath, target, overwrite: true);
        _log.Info($"已把 OptiFine 作为 Mod 安装：{target}");
    }

    // ------------------------------------------------------------------ 响应模型

    private sealed class FabricLoaderEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class FabricGameEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class ForgePromotions
    {
        [System.Text.Json.Serialization.JsonPropertyName("promos")]
        public Dictionary<string, string>? Promos { get; set; }
    }

    private sealed class MavenVersionsPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("versions")]
        public List<string> Versions { get; set; } = new();
    }
}
