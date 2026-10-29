using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;

namespace MCLauncher.App.ViewModels;

/// <summary>导航项。</summary>
public sealed class NavItem
{
    public NavItem(string id, string title, string glyph)
    {
        Id = id;
        Title = title;
        Glyph = glyph;
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>图标字形（Segoe MDL2 Assets 或普通字符）。</summary>
    public string Glyph { get; }

    /// <summary>列表项的自动化名称；屏幕阅读器与 UI 测试都依赖它。</summary>
    public override string ToString() => Title;
}

/// <summary>
/// 外壳 ViewModel：负责导航与状态栏文字。
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly Dictionary<string, object> _pageCache = new();
    private readonly Func<string, object> _pageFactory;
    private object? _currentPage;
    private NavItem? _selectedNavItem;
    private string _statusText = "就绪";

    private bool _hasNotice;
    private string _noticeTitle = string.Empty;
    private string _noticeMessage = string.Empty;
    private string? _noticeUrl;
    private Action? _noticeDismiss;

    private bool _hasUpdate;
    private string _updateTitle = string.Empty;
    private string _updateMessage = string.Empty;
    private string? _updateUrl;
    private Action? _updateDismiss;

    public ShellViewModel(
        IEnumerable<NavItem> navItems,
        Func<string, object> pageFactory,
        object initialPage,
        int initialWidth,
        int initialHeight,
        CrashOverlayViewModel crash,
        SetupViewModel setup)
    {
        _pageFactory = pageFactory;
        NavItems = new ObservableCollection<NavItem>(navItems);
        _currentPage = initialPage;
        _selectedNavItem = NavItems.FirstOrDefault();
        _statusText = _selectedNavItem?.Title ?? "就绪";
        InitialWidth = initialWidth;
        InitialHeight = initialHeight;
        Crash = crash;
        Setup = setup;

        OpenNoticeCommand = new RelayCommand(() => OpenUrl(_noticeUrl));
        DismissNoticeCommand = new RelayCommand(DismissNotice);
        OpenUpdateCommand = new RelayCommand(() => OpenUrl(_updateUrl));
        DismissUpdateCommand = new RelayCommand(DismissUpdate);
    }

    /// <summary>全局崩溃界面，盖在所有页面之上。</summary>
    public CrashOverlayViewModel Crash { get; }

    /// <summary>首次启动的初始化设置，未完成时盖在主界面上。</summary>
    public SetupViewModel Setup { get; }

    public ObservableCollection<NavItem> NavItems { get; }

    public int InitialWidth { get; }

    public int InitialHeight { get; }

    /// <summary>当前页面 ViewModel。</summary>
    public object? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsHomePage));
            }
        }
    }

    /// <summary>当前停在首页：首页不铺白色底板，直接露出背景图。</summary>
    public bool IsHomePage => _currentPage is HomeViewModel;

    /// <summary>选中的导航项；赋值即切页。</summary>
    public NavItem? SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (!SetProperty(ref _selectedNavItem, value) || value is null)
            {
                return;
            }

            Navigate(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ---------------------------------------------------------------- 启动公告 / 更新横幅

    public bool HasNotice
    {
        get => _hasNotice;
        private set
        {
            if (SetProperty(ref _hasNotice, value))
            {
                OnPropertyChanged(nameof(HasNoticeUrl));
                OnPropertyChanged(nameof(HasStartupBanners));
            }
        }
    }

    public string NoticeTitle
    {
        get => _noticeTitle;
        private set => SetProperty(ref _noticeTitle, value);
    }

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public bool HasNoticeUrl => _hasNotice && !string.IsNullOrWhiteSpace(_noticeUrl);

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(HasUpdateUrl));
                OnPropertyChanged(nameof(HasStartupBanners));
            }
        }
    }

    public string UpdateTitle
    {
        get => _updateTitle;
        private set => SetProperty(ref _updateTitle, value);
    }

    public string UpdateMessage
    {
        get => _updateMessage;
        private set => SetProperty(ref _updateMessage, value);
    }

    public bool HasUpdateUrl => _hasUpdate && !string.IsNullOrWhiteSpace(_updateUrl);

    /// <summary>公告或更新任一存在时，横幅区域才占位；都没有时不额外增加顶部间距。</summary>
    public bool HasStartupBanners => _hasNotice || _hasUpdate;

    public ICommand OpenNoticeCommand { get; }

    public ICommand DismissNoticeCommand { get; }

    public ICommand OpenUpdateCommand { get; }

    public ICommand DismissUpdateCommand { get; }

    /// <summary>显示公告横幅；<paramref name="onDismiss"/> 用于记住「不再显示」。</summary>
    public void ShowNotice(string title, string message, string? url, Action? onDismiss = null)
    {
        NoticeTitle = title;
        NoticeMessage = message;
        _noticeUrl = url;
        _noticeDismiss = onDismiss;
        HasNotice = true;
    }

    public void DismissNotice()
    {
        if (!_hasNotice)
        {
            return;
        }

        HasNotice = false;
        var dismiss = _noticeDismiss;
        _noticeDismiss = null;
        dismiss?.Invoke();
    }

    /// <summary>显示新版本横幅；<paramref name="onDismiss"/> 用于记住「稍后提醒」。</summary>
    public void ShowUpdate(string title, string message, string? url, Action? onDismiss = null)
    {
        UpdateTitle = title;
        UpdateMessage = message;
        _updateUrl = url;
        _updateDismiss = onDismiss;
        HasUpdate = true;
    }

    public void DismissUpdate()
    {
        if (!_hasUpdate)
        {
            return;
        }

        HasUpdate = false;
        var dismiss = _updateDismiss;
        _updateDismiss = null;
        dismiss?.Invoke();
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or ArgumentException)
        {
            Debug.WriteLine($"打开链接失败：{url}（{ex.Message}）");
        }
    }

    public ICommand NavigateCommand => new RelayCommand<object>(param =>
    {
        if (param is NavItem item)
        {
            SelectedNavItem = item;
        }
    });

    /// <summary>切换到指定 id 的页面（带缓存，保证页面状态不丢失）。</summary>
    public void NavigateTo(string pageId)
    {
        var item = NavItems.FirstOrDefault(n => n.Id == pageId);
        if (item is not null)
        {
            SelectedNavItem = item;
        }
    }

    /// <summary>
    /// 打开不在顶部导航里的页面（如实例管理）：清空导航选中项，仍复用页面缓存。
    /// </summary>
    public void NavigateToPage(string pageId, string title)
    {
        if (!_pageCache.TryGetValue(pageId, out var page))
        {
            page = _pageFactory(pageId);
            _pageCache[pageId] = page;
        }

        if (_selectedNavItem is not null)
        {
            _selectedNavItem = null;
            OnPropertyChanged(nameof(SelectedNavItem));
        }

        CurrentPage = page;
        StatusText = title;
    }

    private void Navigate(NavItem item)
    {
        if (!_pageCache.TryGetValue(item.Id, out var page))
        {
            page = _pageFactory(item.Id);
            _pageCache[item.Id] = page;
        }

        CurrentPage = page;
        StatusText = item.Title;
    }
}
