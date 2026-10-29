using System.Windows;

namespace MCLauncher.App;

/// <summary>
/// 启动页：在后台预取网络背景图、公告与更新，完成后才显示主窗口。
/// 用户可以随时点「跳过」立即进入主界面。
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = version is null
            ? string.Empty
            : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>用户请求跳过启动准备。</summary>
    public event EventHandler? SkipClicked;

    /// <summary>更新当前阶段文案。</summary>
    public void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        SkipButton.IsEnabled = false;
        SetStatus("正在跳过…");
        SkipClicked?.Invoke(this, EventArgs.Empty);
    }
}
