using System.IO;
using System.Windows;
using MCLauncher.App.Shell;
using MCLauncher.App.ViewModels;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Logging;
using MCLauncher.Core.Services;

namespace MCLauncher.App;

/// <summary>
/// 应用入口：装配服务容器、挂日志、建壳并显示窗口。
/// （不使用 StartupUri，因为窗口需要注入 ViewModel。）
/// </summary>
public partial class App : Application
{
    private const string AppFolderName = "MCLauncher-Reborn";

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
            config.EnsureDefaultGameFolder(AppContext.BaseDirectory);

            Services.RegisterSingleton<ILogger>(log);
            Services.RegisterSingleton<IConfigStore>(store);
            Services.RegisterSingleton(config);

            // ---- 业务服务栈 ----
            var stack = LauncherStack.Create(config, log);
            Stack = stack;
            Services.RegisterSingleton(stack);

            // 没有账号时自动建一个离线账号，保证「启动游戏」开箱可用。
            var account = stack.Accounts.EnsureUsable();
            config.Save();
            log.Info($"当前账号：{account}");

            // 预热版本清单：失败不影响启动（内部已有缓存回退）。
            _ = PrefetchManifestAsync(stack, log);

            // ---- 页面与导航 ----
            var pageFactory = new PageFactory(stack);
            var shellViewModel = new ShellViewModel(
                PageFactory.NavItems,
                pageFactory.Create,
                pageFactory.Create(PageFactory.HomePageId),
                config.Settings.WindowWidth,
                config.Settings.WindowHeight,
                config.Settings.FullMode);

            pageFactory.AttachNavigator(shellViewModel.NavigateTo);
            Services.RegisterSingleton(shellViewModel);

            var window = new MainWindow { DataContext = shellViewModel };

            // 显式设置初始尺寸：Window.Width/Height 的绑定在 DataContext 赋值时序上不可靠，
            // 这里直接按配置赋初值，低于紧凑态下限时钳到 720×450。
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

    private static string GetConfigDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    private static string GetLogDirectory() => Path.Combine(GetConfigDirectory(), "logs");
}
