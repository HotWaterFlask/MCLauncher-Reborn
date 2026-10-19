using System.Collections.Generic;
using MCLauncher.App.ViewModels;

namespace MCLauncher.App.Shell;

/// <summary>
/// 页面注册表：导航项与页面 ViewModel 的唯一映射处。
/// 新增页面时只需改这里 + 加一对 View/ViewModel。
/// </summary>
public sealed class PageFactory
{
    public const string HomePageId = "home";
    public const string VersionsPageId = "versions";
    public const string AccountsPageId = "accounts";
    public const string LaunchLogPageId = "log";
    public const string SettingsPageId = "settings";

    private readonly LauncherStack _stack;
    private Action<string>? _navigate;

    public PageFactory(LauncherStack stack)
    {
        _stack = stack;
    }

    public static IReadOnlyList<NavItem> NavItems { get; } = new[]
    {
        new NavItem(HomePageId, "主页", "\uE80F"),
        new NavItem(VersionsPageId, "版本管理", "\uE896"),
        new NavItem(AccountsPageId, "账号", "\uE77B"),
        new NavItem(LaunchLogPageId, "启动日志", "\uE9D9"),
        new NavItem(SettingsPageId, "设置", "\uE713"),
    };

    /// <summary>
    /// 壳创建好之后把导航回调接进来。
    /// HomeViewModel 里的「设置」按钮是通过 lambda 调用 <see cref="Navigate(string)"/> 的，
    /// 因此这里晚一点注入也来得及。
    /// </summary>
    public void AttachNavigator(Action<string> navigate) => _navigate = navigate;

    public void Navigate(string pageId) => _navigate?.Invoke(pageId);

    public object Create(string pageId) => pageId switch
    {
        VersionsPageId => CreateVersions(),
        AccountsPageId => new AccountsViewModel(_stack),
        LaunchLogPageId => new LaunchLogViewModel(_stack),
        SettingsPageId => new SettingsViewModel(_stack),
        _ => new HomeViewModel(_stack, () => Navigate(SettingsPageId)),
    };

    private VersionsViewModel CreateVersions()
    {
        var viewModel = new VersionsViewModel(_stack);

        // 首次进入版本页时后台加载清单，界面先显示"点击刷新"。
        _ = viewModel.LoadManifestAsync();
        return viewModel;
    }
}
