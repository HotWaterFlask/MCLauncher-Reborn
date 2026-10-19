using System.IO;
using MCLauncher.Core.Logging;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Services.Auth;

namespace MCLauncher.App;

/// <summary>已安装版本的摘要信息。</summary>
public sealed class InstalledVersionInfo
{
    public required string Id { get; init; }

    /// <summary>继承的原版基底（加载器版本才有）。</summary>
    public string? BaseVersionId { get; init; }

    public string? Type { get; init; }

    /// <summary>加载器名称（原版 / Forge / Fabric / Quilt / NeoForge / OptiFine）。</summary>
    public string LoaderName { get; init; } = "原版";

    /// <summary>界面展示用的标题。</summary>
    public string Display => LoaderName == "原版" ? Id : $"{Id}（{LoaderName}）";

    public override string ToString() => Display;
}

/// <summary>
/// 启动器运行时装配：把 Core 的全部服务组装好，并持有当前会话状态
/// （游戏根目录、账号、启动日志缓冲）。界面层只跟它打交道。
/// </summary>
public sealed class LauncherStack : IDisposable
{
    public required Core.Abstractions.ILogger Log { get; init; }

    public required ConfigService Config { get; init; }

    public required AccountService Accounts { get; init; }

    public required HttpDownloader Downloader { get; init; }

    public required DownloadSourceResolver Resolver { get; init; }

    public required DownloadBatch Batch { get; init; }

    public required VersionRepository Repository { get; init; }

    public required VersionManifestService Manifests { get; init; }

    public required GameInstaller Installer { get; init; }

    public required JavaService Java { get; init; }

    public required LaunchArgumentBuilder ArgumentBuilder { get; init; }

    public required GameLauncher Launcher { get; init; }

    public required GameLaunchService LaunchService { get; init; }

    public required InstanceService Instances { get; init; }

    public required ModLoaderService ModLoaders { get; init; }

    public required MicrosoftAuthService MicrosoftAuth { get; init; }

    /// <summary>启动日志缓冲（界面绑定）。</summary>
    public LaunchLogBuffer LaunchLog { get; } = new();

    /// <summary>版本列表 / 账号 / 设置发生变化时触发，供各页面刷新。</summary>
    public event EventHandler? Changed;

    /// <summary>当前游戏根目录（<c>.minecraft</c>）。</summary>
    public string GameRoot =>
        Config.Settings.SelectedGameFolder
        ?? Config.EnsureDefaultGameFolder(AppContext.BaseDirectory);

    public static LauncherStack Create(ConfigService config, Core.Abstractions.ILogger log)
    {
        var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log)
        {
            PreferMirror = config.Settings.PreferMirror,
            MirrorBase = string.IsNullOrWhiteSpace(config.Settings.MirrorBase)
                ? DownloadSourceResolver.DefaultMirrorBase
                : config.Settings.MirrorBase!,
        };

        var batch = new DownloadBatch(downloader, log);
        var repository = new VersionRepository(new VersionResolver(log), log);
        var manifests = new VersionManifestService(downloader, resolver, log, Path.Combine(config.ConfigDirectory, "cache"));
        var installer = new GameInstaller(downloader, resolver, batch, repository, log);
        var java = new JavaService(log);
        var argumentBuilder = new LaunchArgumentBuilder(log);
        var launcher = new GameLauncher(log);

        return new LauncherStack
        {
            Log = log,
            Config = config,
            Accounts = new AccountService(config, log),
            Downloader = downloader,
            Resolver = resolver,
            Batch = batch,
            Repository = repository,
            Manifests = manifests,
            Installer = installer,
            Java = java,
            ArgumentBuilder = argumentBuilder,
            Launcher = launcher,
            LaunchService = new GameLaunchService(repository, installer, java, argumentBuilder, launcher, log),
            Instances = new InstanceService(repository, log),
            ModLoaders = new ModLoaderService(downloader, resolver, repository, installer, java, log),
            MicrosoftAuth = new MicrosoftAuthService(downloader, config, log),
        };
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>扫描本地已安装版本。</summary>
    public IReadOnlyList<InstalledVersionInfo> GetInstalledVersions()
    {
        var result = new List<InstalledVersionInfo>();
        var gameRoot = GameRoot;

        foreach (var id in Repository.ListInstalledIds(gameRoot))
        {
            var json = Repository.TryLoad(gameRoot, id);
            if (json is null)
            {
                continue;
            }

            result.Add(new InstalledVersionInfo
            {
                Id = id,
                BaseVersionId = json.InheritsFrom,
                Type = json.Type,
                LoaderName = DetectLoader(id),
            });
        }

        // 加载器版本排在自己的基底后面，纯原版按版本号倒序。
        result.Sort((a, b) =>
        {
            var byBase = string.Compare(a.BaseVersionId ?? a.Id, b.BaseVersionId ?? b.Id, StringComparison.OrdinalIgnoreCase);
            if (byBase != 0)
            {
                return byBase;
            }

            return string.Compare(b.Id, a.Id, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    /// <summary>从版本 id 推断加载器类型（仅用于界面展示，不影响安装/启动逻辑）。</summary>
    public static string DetectLoader(string versionId)
    {
        var id = versionId.ToLowerInvariant();

        if (id.Contains("-forge") || id.Contains("forge-"))
        {
            return "Forge";
        }

        if (id.Contains("neoforge"))
        {
            return "NeoForge";
        }

        if (id.Contains("quilt"))
        {
            return "Quilt";
        }

        if (id.Contains("fabric"))
        {
            return "Fabric";
        }

        if (id.Contains("optifine"))
        {
            return "OptiFine";
        }

        if (id.Contains("liteloader"))
        {
            return "LiteLoader";
        }

        return "原版";
    }

    /// <summary>实例目录：开启隔离时为 <c>versions/&lt;id&gt;</c>，否则为游戏根目录。</summary>
    public string GetInstanceDirectory(string versionId, bool isolated) =>
        isolated ? VersionRepository.VersionDirectory(GameRoot, versionId) : GameRoot;

    /// <summary>该版本当前是否启用隔离（实例级设置优先，未设置则跟随全局默认）。</summary>
    public bool IsIsolated(string versionId) =>
        Instances.IsIsolated(GameRoot, versionId, Config.Settings.DefaultIsolation);

    /// <summary>从清单下载并补全一个版本（"启动即安装"也走它）。</summary>
    public async Task<InstallReport> InstallVersionAsync(
        string versionId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var entry = await Manifests.FindAsync(versionId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"版本清单里找不到 {versionId}");

        var version = await Manifests.GetVersionJsonAsync(entry, ct).ConfigureAwait(false);
        version.Id = entry.Id;

        var report = await Installer
            .InstallVersionAsync(GameRoot, version, BuildInstallOptions(), progress, ct)
            .ConfigureAwait(false);

        RaiseChanged();
        return report;
    }

    /// <summary>补全（校验并修复）一个已安装版本。</summary>
    public async Task<InstallReport> CompleteVersionAsync(
        string versionId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var report = await Installer
            .EnsureCompleteAsync(GameRoot, versionId, BuildInstallOptions(), progress, ct)
            .ConfigureAwait(false);

        RaiseChanged();
        return report;
    }

    public InstallOptions BuildInstallOptions() => new()
    {
        DownloadAssets = true,
        ExtractNatives = true,
        CreateLauncherProfiles = true,
        MaxParallel = 16,
        VerifyExisting = true,
    };

    public void Dispose() => Downloader.Dispose();
}
