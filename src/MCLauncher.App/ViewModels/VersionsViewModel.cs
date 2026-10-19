using System.Collections.ObjectModel;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

namespace MCLauncher.App.ViewModels;

/// <summary>版本清单里的一项。</summary>
public sealed class RemoteVersionItem : ViewModelBase
{
    private bool _isInstalled;

    public required string Id { get; init; }

    public required string TypeLabel { get; init; }

    public required string ReleaseTime { get; init; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    public string Display => IsInstalled ? $"{Id}　（已安装）" : Id;
}

/// <summary>
/// 版本页：远端清单（可搜索、可筛选快照/远古版本）+ 已安装版本管理。
/// </summary>
public sealed class VersionsViewModel : ViewModelBase
{
    private readonly LauncherStack _stack;
    private readonly AsyncRelayCommand _loadCommand;
    private readonly AsyncRelayCommand _installCommand;
    private readonly AsyncRelayCommand _completeCommand;
    private readonly AsyncRelayCommand _deleteCommand;

    private IReadOnlyList<VersionManifestEntry> _allVersions = Array.Empty<VersionManifestEntry>();
    private HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);

    private string _filterText = string.Empty;
    private bool _showSnapshots;
    private bool _showOldVersions;
    private string _statusText = "点击「刷新列表」从官方清单加载版本";
    private double _progress;
    private bool _isBusy;
    private RemoteVersionItem? _selectedRemote;
    private InstalledVersionInfo? _selectedInstalled;
    private ModLoaderKind _selectedLoaderKind = ModLoaderKind.Fabric;
    private LoaderVersionOption? _selectedLoaderVersion;
    private readonly AsyncRelayCommand _loadLoaderVersionsCommand;
    private readonly AsyncRelayCommand _installLoaderCommand;

    public VersionsViewModel(LauncherStack stack)
    {
        _stack = stack;

        RefreshCommand = new RelayCommand(RefreshInstalled);
        _loadCommand = new AsyncRelayCommand(LoadManifestAsync);
        _installCommand = new AsyncRelayCommand(InstallSelectedAsync, () => !IsBusy && SelectedRemote is not null && !SelectedRemote.IsInstalled);
        _completeCommand = new AsyncRelayCommand(CompleteSelectedAsync, () => !IsBusy && SelectedInstalled is not null);
        _deleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => !IsBusy && SelectedInstalled is not null);
        InstallCommand = _installCommand;
        CompleteCommand = _completeCommand;
        DeleteCommand = _deleteCommand;

        _loadLoaderVersionsCommand = new AsyncRelayCommand(
            LoadLoaderVersionsAsync,
            () => !IsBusy && LoaderTargetVersion is not null);
        _installLoaderCommand = new AsyncRelayCommand(
            InstallLoaderAsync,
            () => !IsBusy && LoaderTargetVersion is not null && SelectedLoaderVersion is not null);
        LoadLoaderVersionsCommand = _loadLoaderVersionsCommand;
        InstallLoaderCommand = _installLoaderCommand;

        _stack.Changed += (_, _) => UiThread.Post(RefreshInstalled);
        RefreshInstalled();
    }

    public ObservableCollection<RemoteVersionItem> RemoteVersions { get; } = new();

    public ObservableCollection<InstalledVersionInfo> InstalledVersions { get; } = new();

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowSnapshots
    {
        get => _showSnapshots;
        set
        {
            if (SetProperty(ref _showSnapshots, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowOldVersions
    {
        get => _showOldVersions;
        set
        {
            if (SetProperty(ref _showOldVersions, value))
            {
                ApplyFilter();
            }
        }
    }

    public RemoteVersionItem? SelectedRemote
    {
        get => _selectedRemote;
        set
        {
            if (SetProperty(ref _selectedRemote, value))
            {
                _installCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public InstalledVersionInfo? SelectedInstalled
    {
        get => _selectedInstalled;
        set
        {
            if (SetProperty(ref _selectedInstalled, value))
            {
                _completeCommand.RaiseCanExecuteChanged();
                _deleteCommand.RaiseCanExecuteChanged();
                _loadLoaderVersionsCommand.RaiseCanExecuteChanged();
                _installLoaderCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsSelectedIsolated));
                OnPropertyChanged(nameof(HasSelectedInstalled));
                OnPropertyChanged(nameof(LoaderTargetVersion));
                OnPropertyChanged(nameof(LoaderTargetText));
            }
        }
    }

    public bool HasSelectedInstalled => SelectedInstalled is not null;

    // ---- 加载器安装 ----

    /// <summary>可安装的加载器种类。</summary>
    public IReadOnlyList<ModLoaderKind> LoaderKinds { get; } = new[]
    {
        ModLoaderKind.Fabric,
        ModLoaderKind.Quilt,
        ModLoaderKind.Forge,
        ModLoaderKind.NeoForge,
    };

    public ObservableCollection<LoaderVersionOption> LoaderVersions { get; } = new();

    public ModLoaderKind SelectedLoaderKind
    {
        get => _selectedLoaderKind;
        set
        {
            if (SetProperty(ref _selectedLoaderKind, value))
            {
                LoaderVersions.Clear();
                SelectedLoaderVersion = null;
                OnPropertyChanged(nameof(LoaderTargetText));
            }
        }
    }

    public LoaderVersionOption? SelectedLoaderVersion
    {
        get => _selectedLoaderVersion;
        set
        {
            if (SetProperty(ref _selectedLoaderVersion, value))
            {
                _installLoaderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>加载器要装在哪个原版版本上：优先用选中实例的基底版本。</summary>
    public string? LoaderTargetVersion =>
        SelectedInstalled is { } installed
            ? installed.BaseVersionId ?? installed.Id
            : null;

    public string LoaderTargetText => LoaderTargetVersion is null
        ? "请先在右侧选中一个已安装版本，加载器会装到它的基底版本上"
        : $"将安装到 {LoaderTargetVersion}（{ModLoaderService.DisplayName(SelectedLoaderKind)}）";

    /// <summary>
    /// 选中版本的版本隔离开关。关闭隔离时 mods 与存档跟原版共享，
    /// 开启后都落在 <c>versions/&lt;id&gt;</c> 下并与其它版本互不影响。
    /// </summary>
    public bool IsSelectedIsolated
    {
        get => SelectedInstalled is not null && _stack.IsIsolated(SelectedInstalled.Id);
        set
        {
            var version = SelectedInstalled;
            if (version is null || value == IsSelectedIsolated)
            {
                return;
            }

            _stack.Instances.SetIsolation(_stack.GameRoot, version.Id, value);
            if (value)
            {
                _stack.Instances.EnsureIsolatedLayout(_stack.GameRoot, version.Id);
            }

            _stack.Config.Save();
            OnPropertyChanged();
            StatusText = value
                ? $"已为 {version.Id} 开启版本隔离（mods / 存档已分家）"
                : $"已为 {version.Id} 关闭版本隔离（与该游戏目录下的其它版本共享 mods 与存档）";
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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

            _loadCommand.RaiseCanExecuteChanged();
            _installCommand.RaiseCanExecuteChanged();
            _completeCommand.RaiseCanExecuteChanged();
            _deleteCommand.RaiseCanExecuteChanged();
            _loadLoaderVersionsCommand.RaiseCanExecuteChanged();
            _installLoaderCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand CompleteCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand LoadLoaderVersionsCommand { get; }

    public ICommand InstallLoaderCommand { get; }

    /// <summary>拉取所选加载器针对基底版本的可用版本。</summary>
    public async Task LoadLoaderVersionsAsync()
    {
        var gameVersion = LoaderTargetVersion;
        if (gameVersion is null)
        {
            return;
        }

        IsBusy = true;
        LoaderVersions.Clear();
        StatusText = $"正在获取 {ModLoaderService.DisplayName(SelectedLoaderKind)} 版本列表…";

        try
        {
            var versions = await Task.Run(() =>
                _stack.ModLoaders.ListLoaderVersionsAsync(SelectedLoaderKind, gameVersion));

            foreach (var option in versions)
            {
                LoaderVersions.Add(option);
            }

            SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.IsRecommended) ?? LoaderVersions.FirstOrDefault();
            StatusText = LoaderVersions.Count == 0
                ? $"{ModLoaderService.DisplayName(SelectedLoaderKind)} 没有支持 {gameVersion} 的版本"
                : $"共 {LoaderVersions.Count} 个 {ModLoaderService.DisplayName(SelectedLoaderKind)} 版本可用于 {gameVersion}";
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"获取加载器版本列表失败（{SelectedLoaderKind}）", ex);
            StatusText = "获取加载器版本失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallLoaderAsync()
    {
        var gameVersion = LoaderTargetVersion;
        var loaderVersion = SelectedLoaderVersion;
        if (gameVersion is null || loaderVersion is null)
        {
            return;
        }

        var kind = SelectedLoaderKind;
        IsBusy = true;
        Progress = 0;
        StatusText = $"正在安装 {ModLoaderService.DisplayName(kind)} {loaderVersion.Version} …";

        try
        {
            var result = await _stack.ModLoaders.InstallAsync(
                new LoaderInstallRequest
                {
                    GameRoot = _stack.GameRoot,
                    GameVersion = gameVersion,
                    Kind = kind,
                    LoaderVersion = loaderVersion.Version,
                    InstallOptions = _stack.BuildInstallOptions(),
                },
                new Progress<InstallProgress>(OnProgress));

            if (result.Success && result.VersionId is not null)
            {
                _stack.Instances.AddLoader(_stack.GameRoot, result.VersionId, $"{ModLoaderService.DisplayName(kind)} {loaderVersion.Version}");
                _stack.Config.Settings.SelectedInstanceId = result.VersionId;
                _stack.Config.Save();

                // 新装的加载器版本默认开隔离，避免 mods 与其它版本相互污染。
                _stack.Instances.SetIsolation(_stack.GameRoot, result.VersionId, true);
                _stack.Instances.EnsureIsolatedLayout(_stack.GameRoot, result.VersionId);

                StatusText = $"已安装 {result.VersionId}（{ModLoaderService.DisplayName(kind)} {loaderVersion.Version}）";
            }
            else
            {
                StatusText = "安装加载器失败：" + result.Error;
            }

            _stack.RaiseChanged();
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"安装加载器失败（{kind} {loaderVersion.Version}）", ex);
            StatusText = "安装加载器失败：" + ex.Message;
        }
        finally
        {
            Progress = 1;
            IsBusy = false;
            UiThread.Post(RefreshInstalled);
        }
    }

    /// <summary>加载远端清单（带磁盘缓存），并刷新筛选结果。</summary>
    public async Task LoadManifestAsync()
    {
        IsBusy = true;
        StatusText = "正在获取版本清单…";
        Progress = 0;

        try
        {
            var manifest = await _stack.Manifests.GetAsync();

            // 清单里同一 id 可能重复（历史遗留），按 id 去重后再展示。
            _allVersions = manifest.Versions
                .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(v => v.ReleaseTime ?? v.Time ?? DateTimeOffset.MinValue)
                .ToList();

            ApplyFilter();
            StatusText = $"清单中共 {_allVersions.Count} 个版本，已显示 {RemoteVersions.Count} 个";
        }
        catch (Exception ex)
        {
            _stack.Log.Error("获取版本清单失败", ex);
            StatusText = "获取版本清单失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        _installedIds = _stack.GetInstalledVersions()
            .Select(v => v.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filter = FilterText.Trim();
        var previousId = SelectedRemote?.Id;

        RemoteVersions.Clear();

        foreach (var entry in _allVersions)
        {
            if (!IsVisibleType(entry.Type))
            {
                continue;
            }

            if (filter.Length > 0 && !entry.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RemoteVersions.Add(new RemoteVersionItem
            {
                Id = entry.Id,
                TypeLabel = DescribeType(entry.Type),
                ReleaseTime = (entry.ReleaseTime ?? entry.Time)?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty,
                IsInstalled = _installedIds.Contains(entry.Id),
            });
        }

        if (previousId is not null)
        {
            SelectedRemote = RemoteVersions.FirstOrDefault(v => v.Id == previousId);
        }
    }

    private bool IsVisibleType(string? type) => type switch
    {
        "snapshot" => ShowSnapshots,
        "old_beta" or "old_alpha" => ShowOldVersions,
        _ => true,
    };

    private static string DescribeType(string? type) => type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => type ?? "未知",
    };

    /// <summary>刷新本地已安装列表。</summary>
    public void RefreshInstalled()
    {
        var previous = SelectedInstalled?.Id;

        InstalledVersions.Clear();
        foreach (var version in _stack.GetInstalledVersions())
        {
            InstalledVersions.Add(version);
        }

        SelectedInstalled = InstalledVersions.FirstOrDefault(v => v.Id == previous) ?? InstalledVersions.FirstOrDefault();

        foreach (var item in RemoteVersions)
        {
            item.IsInstalled = _installedIds.Contains(item.Id);
        }
    }

    private async Task InstallSelectedAsync()
    {
        var item = SelectedRemote;
        if (item is null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = $"正在安装 {item.Id} …";

        try
        {
            var report = await _stack.InstallVersionAsync(item.Id, new Progress<InstallProgress>(OnProgress));

            StatusText = report.Success
                ? $"已安装 {item.Id}：新下载 {report.Downloaded} 个文件，跳过 {report.Skipped} 个"
                : $"安装 {item.Id} 失败：{report.Failed} 个文件下载失败";

            if (report.Success)
            {
                _stack.Config.Settings.SelectedInstanceId = item.Id;
                _stack.Config.Save();
            }
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"安装 {item.Id} 失败", ex);
            StatusText = $"安装 {item.Id} 失败：" + ex.Message;
        }
        finally
        {
            Progress = 1;
            IsBusy = false;
            UiThread.Post(RefreshInstalled);
        }
    }

    private async Task CompleteSelectedAsync()
    {
        var version = SelectedInstalled;
        if (version is null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = $"正在校验并补全 {version.Id} …";

        try
        {
            var report = await _stack.CompleteVersionAsync(version.Id, new Progress<InstallProgress>(OnProgress));
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

    private async Task DeleteSelectedAsync()
    {
        var version = SelectedInstalled;
        if (version is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _stack.Repository.Delete(_stack.GameRoot, version.Id));
            StatusText = $"已删除 {version.Id}";
            UiThread.Post(RefreshInstalled);
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"删除 {version.Id} 失败", ex);
            StatusText = "删除失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnProgress(InstallProgress progress)
    {
        Progress = progress.Fraction;
        StatusText = progress.Total > 0
            ? $"{progress.Stage} {progress.Completed}/{progress.Total}"
            : progress.Stage;
    }
}
