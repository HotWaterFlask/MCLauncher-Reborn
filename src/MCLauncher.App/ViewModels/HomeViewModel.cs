using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Connect;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Utils;

namespace MCLauncher.App.ViewModels;

/// <summary>
/// 主页：背景图直接露出，左上角显示账号，启动后在中间显示输出面板与进度条。
/// 「启动游戏」= 补全缺失文件 → 选 Java → 拼参数 → 起进程。
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private const int MaxOutputChars = 60_000;

    private readonly LauncherStack _stack;
    private readonly AsyncRelayCommand _launchCommand;
    private readonly AsyncRelayCommand _completeCommand;
    private readonly StringBuilder _launchBuilder = new();
    private readonly AvatarService _avatars;
    private readonly DownloadFileRows _launchRows = new();
    private readonly DownloadFileRows _updateRows = new();

    private InstalledVersionInfo? _selectedVersion;
    private string _statusText = "就绪";
    private string _launchOutput = string.Empty;
    private bool _hasLaunchOutput;
    private string _lastStage = string.Empty;
    private bool _suppressLaunchOutput;
    private bool _launchFailed;
    private int _launchSession;
    private double _progress;
    private string _launchSpeedText = string.Empty;
    private bool _isUpdateDialogOpen;
    private bool _isUpdating;
    private double _updateProgress;
    private string _updateSpeedText = string.Empty;
    private string _updateStatus = string.Empty;
    private string _updateResult = string.Empty;
    private bool _hasUpdateResult;
    private bool _updateResultFailed;
    private bool _isBusy;
    private string _accountName = "未选择账号";
    private string _accountTypeText = string.Empty;
    private ImageSource? _accountAvatar;
    private CancellationTokenSource? _avatarCts;
    private string? _avatarUuid;
    private (string Host, int Port)? _pendingServer;
    private bool _windowHiddenByLaunch;

    public HomeViewModel(LauncherStack stack, Action<string?> openInstanceSettings, Action openAccounts)
    {
        _stack = stack;
        _avatars = new AvatarService(stack.Config.ConfigDirectory, stack.Log);

        OpenFolderCommand = new RelayCommand(OpenGameFolder);
        SettingsCommand = new RelayCommand(() => openInstanceSettings(SelectedVersion?.Id));
        OpenAccountsCommand = new RelayCommand(openAccounts);
        RefreshCommand = new RelayCommand(Refresh);
        CloseUpdateDialogCommand = new RelayCommand(() => IsUpdateDialogOpen = false);

        _launchCommand = new AsyncRelayCommand(LaunchAsync, () => !IsBusy && SelectedVersion is not null);
        _completeCommand = new AsyncRelayCommand(CompleteAsync, () => !IsBusy && SelectedVersion is not null);
        LaunchCommand = _launchCommand;
        CompleteCommand = _completeCommand;

        // 游戏 stdout/stderr 在后台线程逐行到达，这里追加到启动面板。
        _stack.LaunchLog.LineAdded += line => UiThread.Post(() => AppendLaunchLine(line));

        _stack.Changed += (_, _) => UiThread.Post(Refresh);
        Refresh();
    }

    public ObservableCollection<InstalledVersionInfo> Versions { get; } = new();

    public InstalledVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value))
            {
                return;
            }

            _stack.Config.Settings.SelectedInstanceId = value?.Id;
            _launchCommand.RaiseCanExecuteChanged();
            _completeCommand.RaiseCanExecuteChanged();

            if (value is not null)
            {
                StatusText = $"已选择 {value.Display}";
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>启动面板里的输出文本，逐行追加，不覆盖旧内容。</summary>
    public string LaunchOutput
    {
        get => _launchOutput;
        private set => SetProperty(ref _launchOutput, value);
    }

    /// <summary>点击启动后才显示中间的输出面板。</summary>
    public bool HasLaunchOutput
    {
        get => _hasLaunchOutput;
        private set => SetProperty(ref _hasLaunchOutput, value);
    }

    /// <summary>启动失败时中间的输出面板变红。</summary>
    public bool IsLaunchFailed
    {
        get => _launchFailed;
        private set => SetProperty(ref _launchFailed, value);
    }

    /// <summary>账号名，显示在头像右侧。</summary>
    public string AccountName
    {
        get => _accountName;
        private set => SetProperty(ref _accountName, value);
    }

    /// <summary>账号类型：离线账户 / 微软账户。</summary>
    public string AccountTypeText
    {
        get => _accountTypeText;
        private set => SetProperty(ref _accountTypeText, value);
    }

    /// <summary>微软账号的皮肤头像；离线账号显示默认皮肤头部。</summary>
    public ImageSource? AccountAvatar
    {
        get => _accountAvatar;
        private set => SetProperty(ref _accountAvatar, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>启动前补文件时的实时下载速度文案。</summary>
    public string LaunchSpeedText
    {
        get => _launchSpeedText;
        private set => SetProperty(ref _launchSpeedText, value);
    }

    /// <summary>启动前补文件时的下载队列（单文件时只有一行）。</summary>
    public ObservableCollection<InstallFileItem> LaunchFiles => _launchRows.Items;

    /// <summary>「更新游戏」的进度弹窗是否打开。</summary>
    public bool IsUpdateDialogOpen
    {
        get => _isUpdateDialogOpen;
        private set => SetProperty(ref _isUpdateDialogOpen, value);
    }

    /// <summary>补全是否仍在进行：进行中不允许关闭弹窗。</summary>
    public bool IsUpdating
    {
        get => _isUpdating;
        private set => SetProperty(ref _isUpdating, value);
    }

    public double UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    /// <summary>「更新游戏」弹窗里的实时下载速度文案。</summary>
    public string UpdateSpeedText
    {
        get => _updateSpeedText;
        private set => SetProperty(ref _updateSpeedText, value);
    }

    /// <summary>「更新游戏」弹窗里的下载队列。</summary>
    public ObservableCollection<InstallFileItem> UpdateFiles => _updateRows.Items;

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public string UpdateResult
    {
        get => _updateResult;
        private set
        {
            if (SetProperty(ref _updateResult, value))
            {
                HasUpdateResult = !string.IsNullOrEmpty(value);
            }
        }
    }

    public bool HasUpdateResult
    {
        get => _hasUpdateResult;
        private set => SetProperty(ref _hasUpdateResult, value);
    }

    /// <summary>补全结果是否为失败，用于把结果文字显示成红色。</summary>
    public bool UpdateResultFailed
    {
        get => _updateResultFailed;
        private set => SetProperty(ref _updateResultFailed, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            _launchCommand.RaiseCanExecuteChanged();
            _completeCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand OpenFolderCommand { get; }

    public ICommand LaunchCommand { get; }

    public ICommand CompleteCommand { get; }

    public ICommand SettingsCommand { get; }

    /// <summary>关闭「更新游戏」的进度弹窗。</summary>
    public ICommand CloseUpdateDialogCommand { get; }

    /// <summary>点击左上角账号块时切到「账号」页。</summary>
    public ICommand OpenAccountsCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>重新扫描本地版本并尽量保持当前选中项。</summary>
    public void Refresh()
    {
        // 设置里的 SelectedInstanceId 是权威值：实例重命名后旧 id 已不存在，不能再用旧的 SelectedVersion.Id。
        var previous = _stack.Config.Settings.SelectedInstanceId ?? SelectedVersion?.Id;

        Versions.Clear();
        foreach (var version in _stack.GetInstalledVersions())
        {
            Versions.Add(version);
        }

        SelectedVersion = Versions.FirstOrDefault(v => v.Id == previous) ?? Versions.FirstOrDefault();

        var account = _stack.Accounts.Selected;
        AccountName = account?.Name ?? "未选择账号";
        AccountTypeText = account is null
            ? "启动时自动创建离线账号"
            : account.IsMicrosoft ? "微软账户"
            : account.IsExternal ? "外置登录"
            : "离线模式";
        UpdateAccountAvatar(account);

        if (Versions.Count == 0)
        {
            StatusText = "本地还没有实例，请到「实例列表」页安装";
        }
    }

    /// <summary>崩溃界面的「重新启动」：用当前选中的实例再启动一次。</summary>
    public void RelaunchSelected()
    {
        if (_launchCommand.CanExecute(null))
        {
            _launchCommand.Execute(null);
        }
    }

    /// <summary>供实例列表的「启动」按钮调用：选中指定实例并立即启动。</summary>
    public void SelectAndLaunch(string versionId) => SelectAndLaunch(versionId, server: null);

    /// <summary>供服务器管理的「加入」调用：启动指定实例并直接进入服务器。</summary>
    public void SelectAndLaunchServer(string versionId, string host, int port) =>
        SelectAndLaunch(versionId, (host, port));

    private void SelectAndLaunch(string versionId, (string Host, int Port)? server)
    {
        _pendingServer = server;
        Refresh();

        var target = Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            _pendingServer = null;
            StatusText = $"找不到实例 {versionId}，可能已被删除。";
            return;
        }

        SelectedVersion = target;
        if (_launchCommand.CanExecute(null))
        {
            _launchCommand.Execute(null);
        }
        else
        {
            _pendingServer = null;
        }
    }

    /// <summary>切换账号时才重新取头像；刷新版本列表等信息时不重复请求。</summary>
    private void UpdateAccountAvatar(Account? account)
    {
        if (string.Equals(_avatarUuid, account?.Uuid, StringComparison.Ordinal))
        {
            return;
        }

        _avatarUuid = account?.Uuid;
        _avatarCts?.Cancel();
        _avatarCts = null;

        if (account is null)
        {
            AccountAvatar = AvatarService.DefaultHeadFor(string.Empty);
            return;
        }

        // 离线账号就是这套默认皮肤；微软/外置账号先顶着它，真实皮肤加载完成后再替换。
        AccountAvatar = AvatarService.DefaultHeadFor(account.Uuid);
        if (!account.IsMicrosoft && !account.IsExternal)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _avatarCts = cts;
        _ = LoadAccountAvatarAsync(account, cts);
    }

    private async Task LoadAccountAvatarAsync(Account account, CancellationTokenSource cts)
    {
        try
        {
            var image = account.IsExternal && !string.IsNullOrWhiteSpace(account.AuthServer)
                ? await _avatars.LoadExternalHeadAsync(account.AuthServer!, account.Uuid, cts.Token).ConfigureAwait(false)
                : await _avatars.LoadMicrosoftHeadAsync(account.Uuid, cts.Token).ConfigureAwait(false);

            if (image is null)
            {
                return;
            }

            UiThread.Post(() =>
            {
                if (!cts.IsCancellationRequested && string.Equals(_avatarUuid, account.Uuid, StringComparison.Ordinal))
                {
                    AccountAvatar = image;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("加载账号头像失败", ex);
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_avatarCts, cts))
            {
                _avatarCts = null;
            }
        }
    }

    /// <summary>
    /// 合并全局设置与实例级覆盖项：实例里填了就用实例的值，留空则跟随全局。
    /// 实例指定了 Java 时顺便探测一次，不可用就明确报错而不是悄悄换一个。
    /// </summary>
    private (LaunchOptions Options, JavaInfo? Java) BuildLaunchOptions(
        InstalledVersionInfo version,
        (string Host, int Port)? server)
    {
        var settings = _stack.Config.Settings;
        var instance = _stack.Instances.Load(_stack.GameRoot, version.Id);

        var width = instance.Width ?? settings.DefaultWidth;
        var height = instance.Height ?? settings.DefaultHeight;
        var mode = instance.WindowMode ?? settings.WindowMode;

        // 最大化：按主显示器分辨率窗口化启动，视觉上等同最大化。
        if (mode == GameWindowMode.Maximized)
        {
            var screen = DisplayMetrics.PrimaryScreenSize();
            if (screen.Width > 0 && screen.Height > 0)
            {
                width = screen.Width;
                height = screen.Height;
            }
        }

        var javaPath = string.IsNullOrWhiteSpace(instance.JavaPath) ? settings.JavaPath : instance.JavaPath;
        JavaInfo? java = null;
        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            java = _stack.Java.Probe(javaPath!);
            if (java is null)
            {
                throw new InvalidOperationException($"指定的 Java 不可用：{javaPath}");
            }
        }

        var quickPlay = ResolveQuickPlay(settings, server);

        var options = new LaunchOptions
        {
            MaxMemoryMb = instance.MaxMemoryMb ?? settings.MaxMemoryMb,
            MinMemoryMb = instance.MinMemoryMb ?? settings.MinMemoryMb,
            Width = width,
            Height = height,
            WindowMode = mode,
            ExtraJvmArgs = string.IsNullOrWhiteSpace(instance.ExtraJvmArgs)
                ? settings.ExtraJvmArgs
                : instance.ExtraJvmArgs,
            ExtraGameArgs = string.IsNullOrWhiteSpace(instance.ExtraGameArgs)
                ? settings.ExtraGameArgs
                : instance.ExtraGameArgs,
            ServerIp = quickPlay.Host,
            ServerPort = quickPlay.Port,
            QuickPlayMode = quickPlay.Mode,
            QuickPlayWorld = quickPlay.World,
            QuickPlayRealmsId = quickPlay.RealmsId,
            WorkingDirectory = settings.LaunchWorkingDirectory,
            EnvironmentVariables = EnvironmentVariables.Parse(settings.EnvironmentVariablesText),
            ProcessPriority = settings.ProcessPriority,
            WrapperCommand = settings.WrapperCommand,
            GameVersion = version.VersionText,
            CompleteBeforeLaunch = !settings.SkipIntegrityCheck,
            LauncherName = "MCLauncher-Reborn",
            LauncherVersion = "6.0",
        };

        return (options, java);
    }

    /// <summary>
    /// 解析快速游玩目标：服务器管理页发起的加入优先于全局设置；
    /// 缺少必要参数或地址无效时退回主菜单，避免把半截设置带进游戏。
    /// </summary>
    private (QuickPlayMode Mode, string? Host, int Port, string? World, string? RealmsId) ResolveQuickPlay(
        AppSettings settings,
        (string Host, int Port)? server)
    {
        if (server is not null)
        {
            return (QuickPlayMode.Multiplayer, server.Value.Host, server.Value.Port, null, null);
        }

        switch (settings.QuickPlayMode)
        {
            case QuickPlayMode.Multiplayer:
                if (string.IsNullOrWhiteSpace(settings.QuickPlayServer))
                {
                    return (QuickPlayMode.None, null, 25565, null, null);
                }

                if (DirectProtocol.TryParseAddress(settings.QuickPlayServer!, out var host, out var port))
                {
                    return (QuickPlayMode.Multiplayer, host, port, null, null);
                }

                _stack.Log.Warn($"快速游玩的服务器地址无效，已忽略：{settings.QuickPlayServer}");
                return (QuickPlayMode.None, null, 25565, null, null);

            case QuickPlayMode.Singleplayer when !string.IsNullOrWhiteSpace(settings.QuickPlayWorld):
                return (QuickPlayMode.Singleplayer, null, 25565, settings.QuickPlayWorld!.Trim(), null);

            case QuickPlayMode.Realms when !string.IsNullOrWhiteSpace(settings.QuickPlayRealmsId):
                return (QuickPlayMode.Realms, null, 25565, null, settings.QuickPlayRealmsId!.Trim());

            default:
                return (QuickPlayMode.None, null, 25565, null, null);
        }
    }

    /// <summary>启动前 / 结束后命令的工作目录：自定义运行路径优先，否则用实例目录。</summary>
    private string ResolveLaunchDirectory(InstalledVersionInfo version)
    {
        var settings = _stack.Config.Settings;
        if (!string.IsNullOrWhiteSpace(settings.LaunchWorkingDirectory))
        {
            var value = settings.LaunchWorkingDirectory!.Trim();
            return Path.IsPathRooted(value) ? value : Path.Combine(_stack.GameRoot, value);
        }

        return _stack.GetInstanceDirectory(version.Id, _stack.IsIsolated(version.Id));
    }

    /// <summary>命令输出同时写入启动日志和界面面板。</summary>
    private void OnCommandOutput(string line)
    {
        _stack.LaunchLog.Append(line);
        _stack.Log.Info($"[命令] {line}");
    }

    /// <summary>
    /// 确保有能运行该版本的 Java：没有时从 Azul 下载对应的 Zulu JDK，
    /// 下载进度复用首页启动面板。显式指定了 Java 时不介入。
    /// </summary>
    private async Task<JavaInfo?> EnsureJavaAsync(InstalledVersionInfo version, JavaInfo? manualJava)
    {
        if (manualJava is not null)
        {
            return manualJava;
        }

        var required = _stack.Repository.Resolve(_stack.GameRoot, version.Id).RequiredJavaMajor;
        var available = await Task.Run(() => _stack.Java.SelectBest(required));
        if (available is not null)
        {
            return available;
        }

        StatusText = $"未找到合适的 Java {required}，正在准备下载 Zulu JDK {required}…";
        AppendLaunchLine($"未找到可用的 Java {required}（64 位），将自动下载官方 Zulu JDK {required}。");
        _stack.Log.Info($"没有可用的 Java {required}，开始从 Azul 下载 Zulu JDK");

        try
        {
            var info = await _stack.ZuluJava.EnsureAsync(
                required,
                new Progress<JavaRuntimeProgress>(OnJavaProgress));

            AppendLaunchLine($"Java {info.MajorVersion} 已就绪：{info.Path}");
            return info;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"缺少 Java {required}，自动下载失败：{ex.Message}", ex);
        }
    }

    private void OnJavaProgress(JavaRuntimeProgress progress)
    {
        Progress = progress.Fraction;
        LaunchSpeedText = InstallFileItem.FormatSpeed(progress.BytesPerSecond);
        _launchRows.Update(progress.ActiveFiles);
        StatusText = progress.Stage;

        if (!string.Equals(_lastStage, progress.Stage, StringComparison.Ordinal))
        {
            _lastStage = progress.Stage;
            AppendLaunchLine(progress.Stage);
        }
    }

    private async Task LaunchAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        var server = _pendingServer;
        _pendingServer = null;

        // 记录启动时刻与实际游戏目录，退出后据此判断是否出现了新的崩溃报告。
        var launchedAt = DateTime.Now;
        var instanceDirectory = _stack.GetInstanceDirectory(version.Id, _stack.IsIsolated(version.Id));

        IsBusy = true;
        Progress = 0;
        LaunchSpeedText = string.Empty;
        _launchRows.Clear();
        IsLaunchFailed = false;
        var session = ++_launchSession;
        _launchBuilder.Clear();
        LaunchOutput = string.Empty;
        HasLaunchOutput = true;
        _suppressLaunchOutput = false;
        _lastStage = string.Empty;
        StatusText = $"正在准备启动 {version.Display} …";
        AppendLaunchLine($"开始启动 {version.Id}");
        _stack.LaunchLog.Clear();

        GameProcess? process = null;

        try
        {
            var account = _stack.Accounts.Selected
                          ?? throw new InvalidOperationException("尚未添加账号，请先在「账号」页添加。");

            // 外置登录的 accessToken 可能已被服务器注销，启动前先校验/续期。
            if (account.IsExternal)
            {
                StatusText = "正在验证外置登录…";
                AppendLaunchLine("正在验证外置登录令牌…");
                account = await _stack.AuthlibAuth.EnsureValidAsync(account);
                _stack.Accounts.Upsert(account);
                _stack.RaiseChanged();
            }

            _stack.Config.Save();

            // 加载器实例依赖的原版基底可能被误删：启动前补齐继承链，避免直接报「找不到被继承的版本」。
            StatusText = "正在检查实例依赖…";
            var repaired = await _stack.EnsureInheritanceChainAsync(
                version.Id,
                new Progress<InstallProgress>(OnInstallProgress));

            if (repaired.Count > 0)
            {
                AppendLaunchLine($"已自动补装缺失的基础版本：{string.Join("、", repaired)}");
            }

            var (options, java) = BuildLaunchOptions(version, server);
            java = await EnsureJavaAsync(version, java);

            var preLaunch = _stack.Config.Settings.PreLaunchCommand;
            if (!string.IsNullOrWhiteSpace(preLaunch))
            {
                var commandDirectory = ResolveLaunchDirectory(version);
                Directory.CreateDirectory(commandDirectory);

                StatusText = "正在执行启动前命令…";
                AppendLaunchLine($"执行启动前命令：{preLaunch}");
                var exitCode = await ShellCommand.RunAsync(preLaunch!, commandDirectory, OnCommandOutput);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"启动前命令执行失败（退出码 {exitCode}）");
                }
            }

            var request = new GameLaunchRequest
            {
                GameRoot = _stack.GameRoot,
                VersionId = version.Id,
                Account = account,
                Options = options,
                Isolated = _stack.IsIsolated(version.Id),
                JavaOverride = java,
                AuthlibCacheDirectory = _stack.Config.ConfigDirectory,
            };

            process = await _stack.LaunchService.LaunchAsync(
                request,
                _stack.LaunchLog,
                new Progress<InstallProgress>(OnInstallProgress));

            Progress = 1;
            LaunchSpeedText = string.Empty;
            _launchRows.Clear();
            StatusText = "等待游戏窗口出现…";
            AppendLaunchLine($"游戏已启动（PID {process.Id}），等待游戏窗口出现…");

            // 进程起来之后游戏还在持续刷 stdout，这些不再显示在首页面板里。
            _suppressLaunchOutput = true;

            // 启动器窗口行为：保持显示 / 隐藏 / 退出 / 隐藏并等游戏结束后再显示。
            ApplyLauncherVisibility();
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"启动 {version.Id} 失败", ex);
            StatusText = "启动失败：" + ex.Message;
            AppendLaunchLine(StatusText);
            Progress = 0;
            LaunchSpeedText = string.Empty;
            _launchRows.Clear();
            IsLaunchFailed = true;
        }
        finally
        {
            IsBusy = false;
        }

        // 进程起来后立刻开始盯着退出，避免等待窗口期间错过崩溃分析。
        if (process is not null)
        {
            _ = WatchExitAsync(process, version, instanceDirectory, launchedAt);
        }

        // 面板统一停留：成功时真正等到游戏窗口出现，失败时让红色错误提示有足够时间被看到。
        await HideLaunchPanelAsync(session, process);
    }

    /// <summary>
    /// 收起启动面板：启动成功时轮询游戏主窗口，窗口真的出现后再停留 3 秒；
    /// 启动失败或进程提前退出时只停留 3 秒，让红色错误提示有时间被看到。
    /// </summary>
    private async Task HideLaunchPanelAsync(int session, GameProcess? process)
    {
        if (process is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        else
        {
            var appeared = await WaitForGameWindowAsync(process, session);
            if (session != _launchSession)
            {
                return;
            }

            if (appeared)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        if (session != _launchSession)
        {
            return;
        }

        HasLaunchOutput = false;
        IsLaunchFailed = false;
        Progress = 0;
        LaunchSpeedText = string.Empty;
        _launchRows.Clear();
        LaunchOutput = string.Empty;
        _launchBuilder.Clear();
    }

    /// <summary>
    /// 轮询游戏进程的主窗口，直到窗口真的出现；
    /// 进程提前退出、用户重新启动或等待超过 5 分钟时返回 false。
    /// </summary>
    private async Task<bool> WaitForGameWindowAsync(GameProcess process, int session)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);

        while (DateTime.UtcNow < deadline)
        {
            if (session != _launchSession || process.HasExited)
            {
                return false;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                StatusText = "游戏窗口已出现，启动完成";
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }

        _stack.Log.Warn("等待游戏窗口出现超时（5 分钟），自动收起启动面板");
        StatusText = "等待游戏窗口超时，详情请看启动日志";
        return false;
    }

    private async Task CompleteAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        IsBusy = true;
        IsUpdating = true;
        UpdateProgress = 0;
        UpdateSpeedText = string.Empty;
        _updateRows.Clear();
        UpdateStatus = "正在校验文件…";
        UpdateResult = string.Empty;
        UpdateResultFailed = false;
        IsUpdateDialogOpen = true;

        try
        {
            var report = await _stack.CompleteVersionAsync(
                version.Id,
                new Progress<InstallProgress>(OnUpdateProgress));

            UpdateProgress = 1;
            UpdateStatus = report.Success ? "校验完成" : "校验结束，仍有文件未能补全";
            UpdateResultFailed = !report.Success;
            UpdateResult = report.Success
                ? $"补全完成：新下载 {report.Downloaded} 个，跳过 {report.Skipped} 个"
                : $"补全失败：{report.Failed} 个文件未能下载";
            StatusText = UpdateResult;
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"补全 {version.Id} 失败", ex);
            UpdateStatus = "校验中断";
            UpdateResultFailed = true;
            UpdateResult = "补全失败：" + ex.Message;
            StatusText = UpdateResult;
        }
        finally
        {
            UpdateSpeedText = string.Empty;
            _updateRows.Clear();
            IsUpdating = false;
            IsBusy = false;
        }
    }

    /// <summary>「更新游戏」弹窗里的进度：阶段、数量与百分比。</summary>
    private void OnUpdateProgress(InstallProgress progress)
    {
        UpdateProgress = progress.Fraction;
        UpdateSpeedText = InstallFileItem.FormatSpeed(progress.BytesPerSecond);
        _updateRows.Update(progress.ActiveFiles);
        UpdateStatus = progress.Total > 0
            ? $"{progress.Stage} {progress.Completed}/{progress.Total}"
            : progress.Stage;
    }

    private void OnInstallProgress(InstallProgress progress)
    {
        // Progress<T> 已经把回调 marshal 到 UI 线程。
        Progress = progress.Fraction;
        LaunchSpeedText = InstallFileItem.FormatSpeed(progress.BytesPerSecond);
        _launchRows.Update(progress.ActiveFiles);
        StatusText = progress.Total > 0
            ? $"{progress.Stage} {progress.Completed}/{progress.Total}"
            : progress.Stage;

        // 只在阶段变化时追加一行，避免几千个文件的进度刷屏。
        if (!string.Equals(_lastStage, progress.Stage, StringComparison.Ordinal))
        {
            _lastStage = progress.Stage;
            AppendLaunchLine($"{progress.Stage}…");
        }
    }

    /// <summary>逐行追加输出，并裁掉过长的历史内容，避免界面越跑越卡。</summary>
    private void AppendLaunchLine(string? line)
    {
        if (_suppressLaunchOutput || string.IsNullOrEmpty(line))
        {
            return;
        }

        _launchBuilder.AppendLine(line);
        if (_launchBuilder.Length > MaxOutputChars)
        {
            _launchBuilder.Remove(0, _launchBuilder.Length - MaxOutputChars);
        }

        LaunchOutput = _launchBuilder.ToString();
    }

    private async Task WatchExitAsync(
        GameProcess process,
        InstalledVersionInfo version,
        string instanceDirectory,
        DateTime launchedAt)
    {
        try
        {
            var exitCode = await process.WaitForExitAsync();
            _stack.Log.Info($"游戏进程已退出：{exitCode}");

            var crash = AnalyzeCrash(exitCode, instanceDirectory, launchedAt);
            var postLaunch = _stack.Config.Settings.PostLaunchCommand;
            var commandDirectory = ResolveLaunchDirectory(version);

            UiThread.Post(() =>
            {
                StatusText = crash is null
                    ? GameLauncher.DescribeExitCode(exitCode)
                    : $"{crash.Title}（退出码 {exitCode}）";
                AppendLaunchLine(StatusText);

                // 崩溃时必须把启动器带回来，否则用户看不到崩溃界面。
                RestoreLauncherWindow(force: crash is not null);

                if (crash is not null)
                {
                    _stack.NotifyGameCrashed(new GameCrashedEventArgs
                    {
                        Report = crash,
                        InstanceId = version.Id,
                        InstanceName = version.Display,
                        InstanceDirectory = instanceDirectory,
                    });
                }
            });

            if (!string.IsNullOrWhiteSpace(postLaunch))
            {
                try
                {
                    _stack.LaunchLog.Append($"执行结束后命令：{postLaunch}");
                    var commandExit = await ShellCommand.RunAsync(postLaunch!, commandDirectory, OnCommandOutput);
                    _stack.LaunchLog.Append($"结束后命令已结束（退出码 {commandExit}）");
                }
                catch (Exception ex)
                {
                    _stack.Log.Warn($"执行结束后命令失败：{postLaunch}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("等待游戏进程退出时出错", ex);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>游戏进程成功启动后，按设置隐藏或关闭启动器。</summary>
    private void ApplyLauncherVisibility()
    {
        var window = Application.Current?.MainWindow;
        if (window is null)
        {
            return;
        }

        switch (_stack.Config.Settings.LauncherVisibility)
        {
            case LauncherVisibilityMode.Hide:
            case LauncherVisibilityMode.HideAndReopen:
                _windowHiddenByLaunch = true;
                window.Hide();
                break;

            case LauncherVisibilityMode.Close:
                _stack.Log.Info("游戏已启动，按设置退出启动器");
                Application.Current!.Shutdown();
                break;
        }
    }

    /// <summary>
    /// 游戏结束后恢复启动器窗口：只有「隐藏并重新打开」会自动恢复；
    /// 崩溃时无论选了哪种隐藏方式都要显示，保证崩溃报告能被看到。
    /// </summary>
    private void RestoreLauncherWindow(bool force)
    {
        var window = Application.Current?.MainWindow;
        if (window is null || !_windowHiddenByLaunch)
        {
            return;
        }

        if (!force && _stack.Config.Settings.LauncherVisibility != LauncherVisibilityMode.HideAndReopen)
        {
            return;
        }

        _windowHiddenByLaunch = false;
        window.Show();
        window.Activate();
    }

    /// <summary>退出码非 0 或本次启动产生了新崩溃报告时返回分析结果。</summary>
    private CrashReport? AnalyzeCrash(int exitCode, string instanceDirectory, DateTime launchedAt)
    {
        try
        {
            return CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = instanceDirectory,
                ExitCode = exitCode,
                LaunchedAt = launchedAt,
                StandardOutput = _stack.LaunchLog.Join(),
            });
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("分析游戏崩溃失败", ex);
            return null;
        }
    }

    private void OpenGameFolder()
    {
        // 版本隔离的实例有自己的目录，其余共享游戏根目录。
        var version = SelectedVersion;
        var folder = version is not null && _stack.IsIsolated(version.Id)
            ? _stack.GetInstanceDirectory(version.Id, isolated: true)
            : _stack.GameRoot;

        try
        {
            Directory.CreateDirectory(folder);

            // 直接让外壳打开目录（比 explorer.exe + 引号参数更稳，也会把窗口带到前台）。
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });

            StatusText = $"已打开 {folder}";
            _stack.Log.Info($"已打开游戏目录：{folder}");
        }
        catch (Exception ex)
        {
            _stack.Log.Warn($"打开游戏目录失败：{folder}", ex);
            StatusText = "打开目录失败：" + ex.Message;
        }
    }
}
