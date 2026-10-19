using System.Collections.ObjectModel;
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
}

/// <summary>
/// 外壳 ViewModel：负责窗口形态（紧凑态 / 完整态）、导航与状态栏文字。
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    /// <summary>宽度达到该值时进入"完整态"（显示导航栏）。</summary>
    public const double FullModeThreshold = 900;

    private readonly Dictionary<string, object> _pageCache = new();
    private readonly Func<string, object> _pageFactory;
    private object? _currentPage;
    private NavItem? _selectedNavItem;
    private bool _fullMode;
    private string _statusText = "就绪";

    public ShellViewModel(
        IEnumerable<NavItem> navItems,
        Func<string, object> pageFactory,
        object initialPage,
        int initialWidth,
        int initialHeight,
        bool initialFullMode)
    {
        _pageFactory = pageFactory;
        NavItems = new ObservableCollection<NavItem>(navItems);
        _currentPage = initialPage;
        InitialWidth = initialWidth;
        InitialHeight = initialHeight;
        _fullMode = initialFullMode;
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public int InitialWidth { get; }

    public int InitialHeight { get; }

    /// <summary>当前页面 ViewModel。</summary>
    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

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

    /// <summary>完整态：显示左侧导航栏与窗口按钮。</summary>
    public bool FullMode
    {
        get => _fullMode;
        private set
        {
            if (SetProperty(ref _fullMode, value))
            {
                OnPropertyChanged(nameof(CompactMode));
            }
        }
    }

    /// <summary>紧凑态：只有背景 + 首页内容层，观感与原版一致。</summary>
    public bool CompactMode => !_fullMode;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand NavigateCommand => new RelayCommand<object>(param =>
    {
        if (param is NavItem item)
        {
            SelectedNavItem = item;
        }
    });

    /// <summary>窗口尺寸变化时调用，用于在紧凑态与完整态之间切换。</summary>
    public void UpdateWindowWidth(double width)
    {
        var shouldBeFull = width >= FullModeThreshold;
        if (shouldBeFull == _fullMode)
        {
            return;
        }

        FullMode = shouldBeFull;
    }

    /// <summary>切换到指定 id 的页面（带缓存，保证页面状态不丢失）。</summary>
    public void NavigateTo(string pageId)
    {
        var item = NavItems.FirstOrDefault(n => n.Id == pageId);
        if (item is not null)
        {
            SelectedNavItem = item;
        }
    }

    private void Navigate(NavItem item)
    {
        if (!_pageCache.TryGetValue(item.Id, out var page))
        {
            page = _pageFactory(item.Id);
            _pageCache[item.Id] = page;
        }

        CurrentPage = page;
        var title = item.Id == NavItems.FirstOrDefault()?.Id ? "主页" : item.Title;
        StatusText = title;
    }
}
