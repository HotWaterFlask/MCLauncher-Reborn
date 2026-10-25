using System.IO;
using System.Windows;
using MCLauncher.App.Shell;
using MCLauncher.App.ViewModels;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Logging;
using MCLauncher.Core.Services;
using MCLauncher.Core.Utils;

namespace MCLauncher.App;

/// <summary>
/// 应用入口：装配服务容器、挂日志、建壳并显示窗口。
/// （不使用 StartupUri，因为窗口需要注入 ViewModel。）
/// </summary>
public partial class App : Application
{
    /// <summary>便携式配置目录：放在启动器所在目录，不再写入用户 AppData。</summary>
    private const string AppFolderName = ".fmmcl";

    public static ServiceContainer Services { get; } = new();

    /// <summary>运行时装配好的服务栈（窗口关闭时释放）。</summary>
    public static LauncherStack? Stack { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Default.AttachSink(new FileLogSink(GetLogDirectory()));
        var log = Log.Default;

        try
        {
            log.Info("================ MCLauncher-Reborn 启动 ================");
            log.Info($"运行目录：{AppContext.BaseDirectory}");

            // ---- 配置 ----
            var store = new JsonConfigStore(GetConfigDirectory(), log);
            var config = new ConfigService(store, log);
            config.Load();
            config.EnsureKnownGameFolders(AppContext.BaseDirectory);
            ApplyFirstRunGameWindowSize(config, log);

            // 背景：默认状态下如果启动器目录已有 bgskin，就自动切到目录模式；不会主动创建该目录。
            var backgroundFolder = BackgroundPicker.FolderPath(AppContext.BaseDirectory);
            if (!config.Settings.BackgroundModeConfigured
                && config.Settings.BackgroundMode == BackgroundMode.None
                && Directory.Exists(backgroundFolder))
            {
                config.Settings.BackgroundMode = BackgroundMode.Folder;
                config.Settings.BackgroundModeConfigured = true;
                log.Info($"检测到 bgskin 目录，背景图切换为目录模式：{backgroundFolder}");
            }

            Services.RegisterSingleton<ILogger>(log);
            Services.RegisterSingleton<IConfigStore>(store);
            Services.RegisterSingleton(config);

            // ---- 业务服务栈 ----
            var stack = LauncherStack.Create(config, log);
            Stack = stack;
            Services.RegisterSingleton(stack);

            // 首次启动不再自动创建离线账号，交给初始化设置让用户自己添加。
            var setup = new SetupViewModel(stack, needsSetup: !config.Settings.SetupCompleted);
            config.Save();

            var account = stack.Accounts.Selected;
            log.Info(account is null ? "尚未添加账号，等待初始化设置" : $"当前账号：{account}");

            // 预热版本清单：失败不影响启动（内部已有缓存回退）。
            _ = PrefetchManifestAsync(stack, log);

            // ---- 页面与导航 ----
            var pageFactory = new PageFactory(stack);
            var homePage = pageFactory.Create(PageFactory.HomePageId);
            var crashOverlay = new CrashOverlayViewModel(stack);

            var shellViewModel = new ShellViewModel(
                PageFactory.NavItems,
                pageFactory.Create,
                homePage,
                config.Settings.WindowWidth,
                config.Settings.WindowHeight,
                crashOverlay,
                setup);

            // 崩溃界面的「重新启动」：回到首页并用当前实例再启动一次。
            crashOverlay.RestartAction = () =>
            {
                shellViewModel.NavigateTo(PageFactory.HomePageId);
                if (homePage is HomeViewModel home)
                {
                    home.RelaunchSelected();
                }
            };

            stack.GameCrashed += (_, args) => crashOverlay.Show(args);

            pageFactory.AttachNavigator(shellViewModel.NavigateTo);
            pageFactory.AttachPageNavigator(shellViewModel.NavigateToPage);
            Services.RegisterSingleton(shellViewModel);

            var window = new MainWindow { DataContext = shellViewModel };

            // 背景：按设置加载（默认底图 / bgskin 目录 / 自定义路径 / 网络 URL）。
            BackgroundPicker.Apply(window, config.Settings, AppContext.BaseDirectory, stack.Downloader, log);

            // 显式设置初始尺寸：Window.Width/Height 的绑定在 DataContext 赋值时序上不可靠，
            // 这里直接按配置赋初值，低于最小尺寸时钳到 720×450。
            window.Width = Math.Max(shellViewModel.InitialWidth, window.MinWidth);
            window.Height = Math.Max(shellViewModel.InitialHeight, window.MinHeight);

            log.Info("外壳已创建，准备显示窗口");
            window.Show();
            log.Info($"窗口已显示：{window.ActualWidth}x{window.ActualHeight}");
        }
        catch (Exception ex)
        {
            log.Error("启动失败", ex);
            MessageBox.Show(
                $"启动器启动失败：{ex.Message}{Environment.NewLine}{Environment.NewLine}详情见日志：{GetLogDirectory()}",
                "MCLauncher-Reborn",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services.TryResolve<ConfigService>(out var config) && config is not null)
        {
            config.Save();
        }

        Stack?.Dispose();
        Log.Default.Info("================ MCLauncher-Reborn 退出 ================");
        base.OnExit(e);
    }

    private static async Task PrefetchManifestAsync(LauncherStack stack, ILogger log)
    {
        try
        {
            await stack.Manifests.GetAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warn("预热版本清单失败（不影响使用）", ex);
        }
    }

    /// <summary>设置页保存背景图配置后，立即刷新窗口背景。</summary>
    public static void RefreshBackground()
    {
        if (Current?.MainWindow is not MainWindow window
            || Stack is null
            || !Services.TryResolve<ConfigService>(out var config)
            || config is null)
        {
            return;
        }

        BackgroundPicker.Apply(window, config.Settings, AppContext.BaseDirectory, Stack.Downloader, Log.Default);
    }

    private static string GetConfigDirectory() =>
        Path.Combine(AppContext.BaseDirectory, AppFolderName);

    /// <summary>
    /// 首次使用（还没有 settings.json）时，按主显示器分辨率挑一个合适的游戏窗口大小。
    /// 只执行这一次，之后一律以用户在设置里的调整为准。
    /// </summary>
    private static void ApplyFirstRunGameWindowSize(ConfigService config, ILogger log)
    {
        if (!config.IsFirstRun)
        {
            return;
        }

        var (screenWidth, screenHeight) = DisplayMetrics.PrimaryScreenSize();
        var (width, height) = DefaultWindowSize.Choose(screenWidth, screenHeight);

        config.Settings.DefaultWidth = width;
        config.Settings.DefaultHeight = height;

        log.Info(screenWidth > 0 && screenHeight > 0
            ? $"首次使用：按显示器 {screenWidth}x{screenHeight} 自动设定游戏窗口 {width}x{height}"
            : $"首次使用：取不到显示器分辨率，游戏窗口使用 {width}x{height}");
    }

    private static string GetLogDirectory() => Path.Combine(GetConfigDirectory(), "logs");
}
