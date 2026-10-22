using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

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

    private InstalledVersionInfo? _selectedVersion;
    private string _statusText = "就绪";
    private string _launchOutput = string.Empty;
    private bool _hasLaunchOutput;
    private string _lastStage = string.Empty;
    private bool _suppressLaunchOutput;
    private double _progress;
    private bool _isBusy;
    private string _accountName = "未选择账号";
    private string _accountTypeText = string.Empty;
    private ImageSource? _accountAvatar;
    private CancellationTokenSource? _avatarCts;
    private string? _avatarUuid;
    private (string Host, int Port)? _pendingServer;

    public HomeViewModel(LauncherStack stack, Action<string?> openInstanceSettings, Action openAccounts)
    {
        _stack = stack;
        _avatars = new AvatarService(stack.Config.ConfigDirectory, stack.Log);

        OpenFolderCommand = new RelayCommand(OpenGameFolder);
        SettingsCommand = new RelayCommand(() => openInstanceSettings(SelectedVersion?.Id));
        OpenAccountsCommand = new RelayCommand(openAccounts);
        RefreshCommand = new RelayCommand(Refresh);

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

    /// <summary>点击左上角账号块时切到「账号」页。</summary>
    public ICommand OpenAccountsCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>重新扫描本地版本并尽量保持当前选中项。</summary>
    public void Refresh()
    {
        var previous = SelectedVersion?.Id ?? _stack.Config.Settings.SelectedInstanceId;

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
            var screen = PrimaryScreenSize();
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
            ServerIp = server?.Host,
            ServerPort = server?.Port ?? 25565,
            CompleteBeforeLaunch = true,
            LauncherName = "MCLauncher-Reborn",
            LauncherVersion = "6.0",
        };

        return (options, java);
    }

    /// <summary>主显示器像素尺寸；取不到时返回 (0, 0)，交给游戏默认值。</summary>
    private static (int Width, int Height) PrimaryScreenSize()
    {
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        return width > 0 && height > 0 ? (width, height) : (0, 0);
    }

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private async Task LaunchAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        var server = _pendingServer;
        _pendingServer = null;

        IsBusy = true;
        Progress = 0;
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
            var account = _stack.Accounts.EnsureUsable();

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

            var (options, java) = BuildLaunchOptions(version, server);
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
            StatusText = "等待游戏窗口出现…";
            AppendLaunchLine($"游戏已启动（PID {process.Id}），等待游戏窗口出现…");

            // 进程起来之后游戏还在持续刷 stdout，这些不再显示在首页面板里。
            _suppressLaunchOutput = true;
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"启动 {version.Id} 失败", ex);
            StatusText = "启动失败：" + ex.Message;
            AppendLaunchLine(StatusText);
        }
        finally
        {
            IsBusy = false;
        }

        if (process is not null)
        {
            await HideLaunchPanelAsync();
            _ = WatchExitAsync(process);
        }
    }

    /// <summary>启动成功后让面板停留 3 秒，然后收起，回到干净的主页。</summary>
    private async Task HideLaunchPanelAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        HasLaunchOutput = false;
        Progress = 0;
        LaunchOutput = string.Empty;
        _launchBuilder.Clear();
    }

    private async Task CompleteAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;

        try
        {
            var report = await _stack.CompleteVersionAsync(
                version.Id,
                new Progress<InstallProgress>(OnInstallProgress));

            StatusText = report.Success
                ? $"补全完成：新下载 {report.Downloaded} 个，跳过 {report.Skipped} 个"
                : $"补全失败：{report.Failed} 个文件未能下载";
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"补全 {version.Id} 失败", ex);
            StatusText = "补全失败：" + ex.Message;
        }
        finally
        {
            Progress = 1;
            IsBusy = false;
        }
    }

    private void OnInstallProgress(InstallProgress progress)
    {
        // Progress<T> 已经把回调 marshal 到 UI 线程。
        Progress = progress.Fraction;
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

    private async Task WatchExitAsync(GameProcess process)
    {
        try
        {
            var exitCode = await process.WaitForExitAsync();
            _stack.Log.Info($"游戏进程已退出：{exitCode}");
            UiThread.Post(() =>
            {
                StatusText = GameLauncher.DescribeExitCode(exitCode);
                AppendLaunchLine(StatusText);
            });
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

    private void OpenGameFolder()
    {
        var folder = _stack.GameRoot;

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
