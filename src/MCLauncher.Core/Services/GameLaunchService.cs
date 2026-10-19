using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Logging;
using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>一次启动请求。</summary>
public sealed class GameLaunchRequest
{
    /// <summary>游戏根目录（<c>.minecraft</c>）。</summary>
    public required string GameRoot { get; init; }

    public required string VersionId { get; init; }

    public required Account Account { get; init; }

    public LaunchOptions Options { get; init; } = new();

    /// <summary>是否版本隔离（true 时游戏目录为 <c>versions/&lt;id&gt;</c>，mods/存档都在里面）。</summary>
    public bool Isolated { get; init; }

    /// <summary>指定 Java；为空时自动按版本要求挑选。</summary>
    public JavaInfo? JavaOverride { get; init; }
}

/// <summary>
/// 「启动游戏」按钮背后的完整流程：补全文件 → 选 Java → 拼参数 → 起进程。
/// 把这条链放在 Core 里，界面层只剩一次调用。
/// </summary>
public sealed class GameLaunchService
{
    private readonly VersionRepository _repository;
    private readonly GameInstaller _installer;
    private readonly JavaService _javaService;
    private readonly LaunchArgumentBuilder _argumentBuilder;
    private readonly GameLauncher _launcher;
    private readonly ILogger _log;

    public GameLaunchService(
        VersionRepository repository,
        GameInstaller installer,
        JavaService javaService,
        LaunchArgumentBuilder argumentBuilder,
        GameLauncher launcher,
        ILogger log)
    {
        _repository = repository;
        _installer = installer;
        _javaService = javaService;
        _argumentBuilder = argumentBuilder;
        _launcher = launcher;
        _log = log;
    }

    public async Task<GameProcess> LaunchAsync(
        GameLaunchRequest request,
        LaunchLogBuffer logBuffer,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_repository.IsInstalled(request.GameRoot, request.VersionId))
        {
            throw new InvalidOperationException($"本地没有安装版本 {request.VersionId}，请先安装");
        }

        if (request.Options.CompleteBeforeLaunch)
        {
            var report = await _installer
                .EnsureCompleteAsync(
                    request.GameRoot,
                    request.VersionId,
                    new InstallOptions { DownloadAssets = true },
                    progress,
                    ct)
                .ConfigureAwait(false);

            if (!report.Success)
            {
                throw new InvalidOperationException(
                    $"启动前补全文件失败（{report.Failed} 个文件）：{string.Join("；", report.Failures.Take(5).Select(f => f.Name))}");
            }
        }

        var version = _repository.Resolve(request.GameRoot, request.VersionId);
        var jarId = VersionRepository.ResolveJarId(version);
        var clientJarPath = VersionRepository.VersionJarPath(request.GameRoot, jarId);

        if (!File.Exists(clientJarPath))
        {
            throw new FileNotFoundException($"客户端 jar 缺失：{clientJarPath}");
        }

        var indexInfo = version.AssetIndex;
        var index = indexInfo is null ? null : _installer.TryLoadAssetIndex(request.GameRoot, indexInfo);
        var assetsRoot = GameInstaller.ResolveAssetsRoot(request.GameRoot, indexInfo, index);

        var java = request.JavaOverride ?? _javaService.SelectBest(version.RequiredJavaMajor);
        if (java is null)
        {
            throw new InvalidOperationException(
                $"没有找到可用的 Java {version.RequiredJavaMajor}（64 位）。请在设置页指定 Java 路径，或安装对应版本。");
        }

        var logConfigPath = version.Logging?.Client?.File?.Id is { Length: > 0 } logId
            ? Path.Combine(GameInstaller.LogConfigDirectory(request.GameRoot), logId)
            : null;

        if (logConfigPath is not null && !File.Exists(logConfigPath))
        {
            logConfigPath = null;
        }

        var context = new LaunchContext
        {
            Version = version,
            Account = request.Account,
            Options = request.Options,
            JavaPath = java.Path,
            VersionId = request.VersionId,
            GameDirectory = request.Isolated
                ? VersionRepository.VersionDirectory(request.GameRoot, request.VersionId)
                : request.GameRoot,
            AssetsRoot = assetsRoot,
            NativesDirectory = VersionRepository.NativesDirectory(request.GameRoot, request.VersionId),
            LibrariesDirectory = GameInstaller.LibrariesDirectory(request.GameRoot),
            ClientJarPath = clientJarPath,
            AssetsIndexName = indexInfo?.Id,
            LogConfigPath = logConfigPath,
        };

        if (request.Isolated)
        {
            PathUtil.EnsureDirectory(context.GameDirectory);
        }

        var command = _argumentBuilder.Build(context);
        _log.Info($"使用 Java {java.MajorVersion}（{java.Path}）启动 {request.VersionId}");

        return _launcher.Start(command, logBuffer, java.MajorVersion);
    }
}
