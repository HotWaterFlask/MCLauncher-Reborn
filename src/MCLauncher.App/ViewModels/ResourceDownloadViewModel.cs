using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;

namespace MCLauncher.App.ViewModels;

/// <summary>排序下拉项。</summary>
public sealed class DownloadSortOption
{
    public required string Label { get; init; }

    public required CurseForgeSort Sort { get; init; }
}

/// <summary>下载列表里的一行项目。</summary>
public sealed class CurseForgeProjectRow : ViewModelBase
{
    private ImageSource? _icon;

    public CurseForgeProjectRow(CurseForgeProject project)
    {
        Project = project;
    }

    public CurseForgeProject Project { get; }

    public string Name => Project.Name;

    public string Summary => Project.Summary;

    public string DownloadText => Project.DownloadText + " 次下载";

    public IReadOnlyList<string> Categories => Project.Categories;

    public bool HasAuthor => !string.IsNullOrWhiteSpace(Project.Author);

    public string AuthorText => "作者：" + Project.Author;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon is not null;
}

/// <summary>
/// 资源下载对话框：按名称 / 游戏版本 / 排序检索 CurseForge，
/// 选中项目后自动挑一个与实例版本、加载器兼容的文件下载。
/// </summary>
public sealed class ResourceDownloadViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private const string AllVersions = "全部";

    private readonly LauncherStack _stack;
    private readonly AsyncRelayCommand _searchCommand;
    private readonly AsyncRelayCommand<CurseForgeProjectRow> _downloadCommand;
    private readonly RelayCommand _previousPageCommand;
    private readonly RelayCommand _nextPageCommand;

    private ResourceKind _kind = ResourceKind.Mod;
    private string _instanceId = string.Empty;
    private string _instanceDirectory = string.Empty;
    private string? _gameVersion;
    private string? _loaderName;

    private bool _isOpen;
    private bool _isBusy;
    private bool _isDownloading;
    private string _title = "下载";
    private string _statusText = string.Empty;
    private string _searchText = string.Empty;
    private string? _selectedGameVersion;
    private DownloadSortOption? _selectedSort;
    private int _pageIndex;
    private int _totalPages;
    private int _totalCount;
    private double _downloadProgress;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _downloadCts;

    public ResourceDownloadViewModel(LauncherStack stack)
    {
        _stack = stack;

        SortOptions = new[]
        {
            new DownloadSortOption { Label = "热度", Sort = CurseForgeSort.Popularity },
            new DownloadSortOption { Label = "最近更新", Sort = CurseForgeSort.LastUpdated },
            new DownloadSortOption { Label = "总下载量", Sort = CurseForgeSort.TotalDownloads },
            new DownloadSortOption { Label = "名称", Sort = CurseForgeSort.Name },
        };
        _selectedSort = SortOptions[0];

        _searchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        _downloadCommand = new AsyncRelayCommand<CurseForgeProjectRow>(DownloadAsync, row => !IsBusy && row is not null);
        _previousPageCommand = new RelayCommand(
            () => _ = LoadPageAsync(_pageIndex - 1),
            () => !IsBusy && _pageIndex > 0);
        _nextPageCommand = new RelayCommand(
            () => _ = LoadPageAsync(_pageIndex + 1),
            () => !IsBusy && _pageIndex + 1 < _totalPages);

        SearchCommand = _searchCommand;
        DownloadCommand = _downloadCommand;
        PreviousPageCommand = _previousPageCommand;
        NextPageCommand = _nextPageCommand;
        CloseCommand = new RelayCommand(Close);
    }

    /// <summary>下载完成后通知实例管理页刷新列表。</summary>
    public event Action? ResourceDownloaded;

    public ObservableCollection<CurseForgeProjectRow> Results { get; } = new();

    public ObservableCollection<string> GameVersionOptions { get; } = new();

    public IReadOnlyList<DownloadSortOption> SortOptions { get; }

    public ICommand SearchCommand { get; }

    public ICommand DownloadCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand CloseCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string? SelectedGameVersion
    {
        get => _selectedGameVersion;
        set => SetProperty(ref _selectedGameVersion, value);
    }

    public DownloadSortOption? SelectedSort
    {
        get => _selectedSort;
        set => SetProperty(ref _selectedSort, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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

            OnPropertyChanged(nameof(CanPage));
            _searchCommand.RaiseCanExecuteChanged();
            _downloadCommand.RaiseCanExecuteChanged();
            _previousPageCommand.RaiseCanExecuteChanged();
            _nextPageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set => SetProperty(ref _isDownloading, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, value);
    }

    public bool CanPage => !IsBusy && Results.Count > 0;

    public string PageText => _totalCount == 0
        ? "暂无结果"
        : $"第 {_pageIndex + 1} / {Math.Max(_totalPages, 1)} 页 · 共 {_totalCount} 个";

    /// <summary>打开对话框（由实例管理页调用）。</summary>
    public void Show(
        ResourceKind kind,
        string instanceId,
        string instanceDirectory,
        string? gameVersion,
        string? loaderName)
    {
        _kind = kind;
        _instanceId = instanceId;
        _instanceDirectory = instanceDirectory;
        _gameVersion = string.IsNullOrWhiteSpace(gameVersion) ? null : gameVersion.Trim();
        _loaderName = loaderName;

        Title = "下载" + ResourceRepository.DisplayName(kind);
        SearchText = string.Empty;
        IsOpen = true;
        Results.Clear();
        _pageIndex = 0;
        _totalPages = 0;
        _totalCount = 0;
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPage));

        GameVersionOptions.Clear();
        GameVersionOptions.Add(AllVersions);
        if (_gameVersion is not null)
        {
            GameVersionOptions.Add(_gameVersion);
        }

        SelectedGameVersion = _gameVersion ?? AllVersions;
        SelectedSort = SortOptions[0];
        _ = LoadPageAsync(0);
    }

    public void Close()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _downloadCts?.Cancel();
        _downloadCts = null;
        IsOpen = false;
        IsBusy = false;
        IsDownloading = false;
        DownloadProgress = 0;
    }

    // ---------------------------------------------------------------- 搜索

    private Task SearchAsync()
    {
        _pageIndex = 0;
        return LoadPageAsync(0);
    }

    private async Task LoadPageAsync(int pageIndex)
    {
        if (!IsOpen)
        {
            return;
        }

        if (!_stack.CurseForge.IsConfigured)
        {
            StatusText = "未配置 CurseForge API Key，无法检索。";
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        _pageIndex = Math.Max(pageIndex, 0);
        IsBusy = true;
        StatusText = "正在检索…";
        Results.Clear();
        OnPropertyChanged(nameof(PageText));

        try
        {
            var gameVersion = string.Equals(SelectedGameVersion, AllVersions, StringComparison.Ordinal)
                ? null
                : SelectedGameVersion;

            var page = await _stack.CurseForge.SearchAsync(
                _kind,
                SearchText,
                gameVersion,
                SelectedSort?.Sort ?? CurseForgeSort.Popularity,
                _pageIndex,
                PageSize,
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            _totalCount = page.TotalCount;
            _totalPages = page.TotalPages;

            foreach (var project in page.Projects)
            {
                var row = new CurseForgeProjectRow(project);
                Results.Add(row);
                _ = LoadIconAsync(row, cts.Token);
            }

            StatusText = page.Projects.Count == 0
                ? "没有找到结果，换个关键词或把游戏版本切到「全部」试试。"
                : $"共 {_totalCount} 个结果。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("CurseForge 检索失败", ex);
            StatusText = "检索失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsBusy = false;
                _searchCts = null;
            }

            cts.Dispose();
            OnPropertyChanged(nameof(PageText));
            OnPropertyChanged(nameof(CanPage));
            _previousPageCommand.RaiseCanExecuteChanged();
            _nextPageCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task LoadIconAsync(CurseForgeProjectRow row, CancellationToken ct)
    {
        var url = row.Project.LogoUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var bytes = await _stack.Downloader.GetBytesAsync(url!, ct).ConfigureAwait(false);
            var image = DecodeImage(bytes);
            if (image is null)
            {
                return;
            }

            UiThread.Post(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    row.Icon = image;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpApiException or HttpRequestException or IOException)
        {
            _stack.Log.Trace($"加载项目图标失败：{url}");
        }
    }

    private static ImageSource? DecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- 下载

    private async Task DownloadAsync(CurseForgeProjectRow? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        var gameVersion = string.Equals(SelectedGameVersion, AllVersions, StringComparison.Ordinal)
            ? null
            : SelectedGameVersion;

        IsBusy = true;
        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = $"正在获取 {row.Name} 的文件…";

        _downloadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _downloadCts = cts;

        try
        {
            var file = await _stack.CurseForge.PickBestFileAsync(
                row.Project.Id, _kind, gameVersion, _loaderName, cts.Token);

            if (file is null)
            {
                StatusText = "没有找到适用于当前游戏版本 / 加载器的文件。";
                return;
            }

            var result = await _stack.ResourceDownloads.DownloadAsync(
                new ResourceDownloadRequest
                {
                    Kind = _kind,
                    File = file,
                    InstanceDirectory = _instanceDirectory,
                },
                new Progress<InstallProgress>(progress =>
                {
                    DownloadProgress = progress.Total > 0
                        ? (double)progress.Completed / progress.Total
                        : 0;
                    StatusText = progress.Stage;
                }),
                cts.Token);

            if (result.Success)
            {
                DownloadProgress = 1;
                StatusText = $"已下载 {file.FileName}。";
                _stack.Log.Info($"下载 {ResourceRepository.DisplayName(_kind)}：{file.FileName}");
                ResourceDownloaded?.Invoke();
            }
            else
            {
                StatusText = result.Error ?? "下载失败。";
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _stack.Log.Error("下载资源失败", ex);
            StatusText = "下载失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_downloadCts, cts))
            {
                _downloadCts = null;
            }

            cts.Dispose();
            IsBusy = false;
            IsDownloading = false;
        }
    }
}
