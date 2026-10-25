using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;

namespace MCLauncher.App.ViewModels;

/// <summary>「版本列表源 / 下载源」下拉里的一项。</summary>
public sealed class DownloadSourceChoice
{
    public required DownloadSourcePreference Value { get; init; }

    public required string Label { get; init; }
}

/// <summary>附加内容默认下载源下拉里的一项。</summary>
public sealed class ContentSourceChoice
{
    public required ResourceSource Value { get; init; }

    public required string Label { get; init; }
}

/// <summary>
/// 设置页：只放全局设置。
/// 实例的独立设置由实例管理页的「游戏设置」在右侧原地展示。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private static readonly Regex WindowSizePattern =
        new(@"^\s*(\d{2,5})\s*[xX×*]\s*(\d{2,5})\s*$", RegexOptions.Compiled);

    private readonly LauncherStack _stack;
    private readonly AsyncRelayCommand _detectJavaCommand;

    private string _statusText = "修改后点「保存设置」生效";
    private bool _isBusy;

    public SettingsViewModel(LauncherStack stack)
    {
        _stack = stack;

        SaveCommand = new RelayCommand(Save);
        BrowseGameFolderCommand = new RelayCommand(BrowseGameFolder);
        BrowseJavaCommand = new RelayCommand(BrowseJava);
        BrowseBackgroundFileCommand = new RelayCommand(BrowseBackgroundFile);
        BrowseBackgroundFolderCommand = new RelayCommand(BrowseBackgroundFolder);
        OpenBackgroundFolderCommand = new RelayCommand(OpenBackgroundFolder);
        _detectJavaCommand = new AsyncRelayCommand(DetectJavaAsync);
        DetectJavaCommand = _detectJavaCommand;

        foreach (var java in _stack.Config.Settings.DetectedJavas)
        {
            Javas.Add(java);
        }
    }

    // ---------------------------------------------------------------- 全局设置

    public ObservableCollection<DetectedJava> Javas { get; } = new();

    public int MaxMemoryMb
    {
        get => _stack.Config.Settings.MaxMemoryMb;
        set
        {
            if (_stack.Config.Settings.MaxMemoryMb == value)
            {
                return;
            }

            _stack.Config.Settings.MaxMemoryMb = value;
            OnPropertyChanged();
        }
    }

    public int MinMemoryMb
    {
        get => _stack.Config.Settings.MinMemoryMb;
        set
        {
            if (_stack.Config.Settings.MinMemoryMb == value)
            {
                return;
            }

            _stack.Config.Settings.MinMemoryMb = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<DownloadSourceChoice> DownloadSourceChoices { get; } = new[]
    {
        new DownloadSourceChoice
        {
            Value = DownloadSourcePreference.Auto,
            Label = "自动选择下载源",
        },
        new DownloadSourceChoice
        {
            Value = DownloadSourcePreference.Official,
            Label = "尽量使用官方源",
        },
        new DownloadSourceChoice
        {
            Value = DownloadSourcePreference.Mirror,
            Label = "尽量使用镜像源",
        },
    };

    public IReadOnlyList<ContentSourceChoice> ContentSourceChoices { get; } = new[]
    {
        new ContentSourceChoice { Value = ResourceSource.Modrinth, Label = "Modrinth" },
        new ContentSourceChoice { Value = ResourceSource.CurseForge, Label = "CurseForge" },
    };

    /// <summary>版本清单、版本 JSON 的下载源。</summary>
    public DownloadSourcePreference VersionListSource
    {
        get => _stack.Config.Settings.VersionListSource;
        set
        {
            if (_stack.Config.Settings.VersionListSource == value)
            {
                return;
            }

            _stack.Config.Settings.VersionListSource = value;
            _stack.Resolver.VersionListSource = value;
            OnPropertyChanged();
        }
    }

    /// <summary>客户端、依赖库、资源对象等游戏文件的下载源。</summary>
    public DownloadSourcePreference FileSource
    {
        get => _stack.Config.Settings.FileSource;
        set
        {
            if (_stack.Config.Settings.FileSource == value)
            {
                return;
            }

            _stack.Config.Settings.FileSource = value;
            _stack.Resolver.FileSource = value;
            OnPropertyChanged();
        }
    }

    /// <summary>模组 / 资源包 / 光影等附加内容的默认源。</summary>
    public ResourceSource DefaultContentSource
    {
        get => _stack.Config.Settings.DefaultContentSource;
        set
        {
            if (_stack.Config.Settings.DefaultContentSource == value)
            {
                return;
            }

            _stack.Config.Settings.DefaultContentSource = value;
            OnPropertyChanged();
        }
    }

    public bool AutoDownloadThreads
    {
        get => _stack.Config.Settings.AutoDownloadThreads;
        set
        {
            if (_stack.Config.Settings.AutoDownloadThreads == value)
            {
                return;
            }

            _stack.Config.Settings.AutoDownloadThreads = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CustomDownloadThreads));
            OnPropertyChanged(nameof(DownloadThreadsText));
        }
    }

    public bool CustomDownloadThreads
    {
        get => !_stack.Config.Settings.AutoDownloadThreads;
        set => AutoDownloadThreads = !value;
    }

    public int DownloadThreads
    {
        get => Math.Clamp(_stack.Config.Settings.DownloadThreads, 1, AppSettings.MaxThreadCount);
        set
        {
            var clamped = Math.Clamp(value, 1, AppSettings.MaxThreadCount);
            if (_stack.Config.Settings.DownloadThreads == clamped)
            {
                return;
            }

            _stack.Config.Settings.DownloadThreads = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DownloadThreadsText));
        }
    }

    public string DownloadThreadsText => _stack.Config.Settings.AutoDownloadThreads
        ? $"自动（{AppSettings.AutoThreadCount} 线程）"
        : $"{_stack.Config.Settings.DownloadThreads} 线程";

    // ---------------------------------------------------------------- 背景图

    public string BackgroundFolderPath => BackgroundPicker.FolderPath(AppContext.BaseDirectory);

    public bool IsBackgroundDefault
    {
        get => _stack.Config.Settings.BackgroundMode == BackgroundMode.None;
        set
        {
            if (value)
            {
                SetBackgroundMode(BackgroundMode.None);
            }
        }
    }

    public bool IsBackgroundFolder
    {
        get => _stack.Config.Settings.BackgroundMode == BackgroundMode.Folder;
        set
        {
            if (value)
            {
                SetBackgroundMode(BackgroundMode.Folder);
            }
        }
    }

    public bool IsBackgroundCustom
    {
        get => _stack.Config.Settings.BackgroundMode == BackgroundMode.Custom;
        set
        {
            if (value)
            {
                SetBackgroundMode(BackgroundMode.Custom);
            }
        }
    }

    public bool IsBackgroundNetwork
    {
        get => _stack.Config.Settings.BackgroundMode == BackgroundMode.Network;
        set
        {
            if (value)
            {
                SetBackgroundMode(BackgroundMode.Network);
            }
        }
    }

    /// <summary>自定义背景：目录（随机取图）或单张图片路径。</summary>
    public string BackgroundCustomPath
    {
        get => _stack.Config.Settings.BackgroundCustomPath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.BackgroundCustomPath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.BackgroundCustomPath = normalized;
            OnPropertyChanged();
        }
    }

    public string BackgroundUrl
    {
        get => _stack.Config.Settings.BackgroundUrl ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.BackgroundUrl, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.BackgroundUrl = normalized;
            OnPropertyChanged();
        }
    }

    private void SetBackgroundMode(BackgroundMode mode)
    {
        var settings = _stack.Config.Settings;
        if (settings.BackgroundMode == mode)
        {
            return;
        }

        settings.BackgroundMode = mode;
        settings.BackgroundModeConfigured = true;

        if (mode == BackgroundMode.Folder)
        {
            BackgroundPicker.EnsureFolder(AppContext.BaseDirectory, _stack.Log);
        }

        OnPropertyChanged(nameof(IsBackgroundDefault));
        OnPropertyChanged(nameof(IsBackgroundFolder));
        OnPropertyChanged(nameof(IsBackgroundCustom));
        OnPropertyChanged(nameof(IsBackgroundNetwork));
    }

    public bool DefaultIsolation
    {
        get => _stack.Config.Settings.DefaultIsolation;
        set
        {
            if (_stack.Config.Settings.DefaultIsolation == value)
            {
                return;
            }

            _stack.Config.Settings.DefaultIsolation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>窗口大小的常用预设；下拉框允许直接输入任意「宽x高」。</summary>
    public IReadOnlyList<string> WindowSizePresets { get; } = new[]
    {
        "854x480",
        "1280x720",
        "1600x900",
        "1920x1080",
        "2560x1440",
        "3840x2160",
    };

    public bool IsWindowed
    {
        get => _stack.Config.Settings.WindowMode == GameWindowMode.Windowed;
        set
        {
            if (value)
            {
                SetWindowMode(GameWindowMode.Windowed);
            }
        }
    }

    public bool IsFullscreen
    {
        get => _stack.Config.Settings.WindowMode == GameWindowMode.Fullscreen;
        set
        {
            if (value)
            {
                SetWindowMode(GameWindowMode.Fullscreen);
            }
        }
    }

    public bool IsMaximized
    {
        get => _stack.Config.Settings.WindowMode == GameWindowMode.Maximized;
        set
        {
            if (value)
            {
                SetWindowMode(GameWindowMode.Maximized);
            }
        }
    }

    /// <summary>窗口化尺寸，格式「宽x高」；选预设或直接改数字都走这里。</summary>
    public string GameWindowSize
    {
        get
        {
            var settings = _stack.Config.Settings;
            return settings.DefaultWidth > 0 && settings.DefaultHeight > 0
                ? $"{settings.DefaultWidth}x{settings.DefaultHeight}"
                : "854x480";
        }
        set
        {
            var match = WindowSizePattern.Match(value ?? string.Empty);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out var width)
                || !int.TryParse(match.Groups[2].Value, out var height)
                || width < 100 || height < 100)
            {
                // 输入不合法：还原成当前有效值（失焦时才触发，不会打断输入过程）。
                OnPropertyChanged();
                return;
            }

            var settings = _stack.Config.Settings;
            if (settings.DefaultWidth == width && settings.DefaultHeight == height)
            {
                return;
            }

            settings.DefaultWidth = width;
            settings.DefaultHeight = height;
            OnPropertyChanged();
        }
    }

    private void SetWindowMode(GameWindowMode mode)
    {
        var settings = _stack.Config.Settings;
        if (settings.WindowMode == mode)
        {
            return;
        }

        settings.WindowMode = mode;
        OnPropertyChanged(nameof(IsWindowed));
        OnPropertyChanged(nameof(IsFullscreen));
        OnPropertyChanged(nameof(IsMaximized));
    }

    public string GameFolder
    {
        get => _stack.Config.Settings.SelectedGameFolder ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : LauncherStack.NormalizeGameFolder(value.Trim());

            if (string.Equals(_stack.Config.Settings.SelectedGameFolder, normalized, StringComparison.Ordinal))
            {
                return;
            }

            if (normalized is not null && Directory.Exists(normalized))
            {
                // 有效目录直接交给栈处理：会补进目录列表并保存/通知其它页面。
                _stack.SelectGameFolder(normalized);
            }
            else
            {
                _stack.Config.Settings.SelectedGameFolder = normalized;
                _stack.Config.Save();
            }

            OnPropertyChanged();
        }
    }

    public string JavaPath
    {
        get => _stack.Config.Settings.JavaPath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.JavaPath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.JavaPath = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(JavaHint));
        }
    }

    public string JavaHint => string.IsNullOrWhiteSpace(_stack.Config.Settings.JavaPath)
        ? "留空时按版本要求自动挑选已探测到的 Java。"
        : "已手动指定，启动时将优先使用它。";

    public string ExtraJvmArgs
    {
        get => _stack.Config.Settings.ExtraJvmArgs ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.ExtraJvmArgs, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.ExtraJvmArgs = normalized;
            OnPropertyChanged();
        }
    }

    public string ExtraGameArgs
    {
        get => _stack.Config.Settings.ExtraGameArgs ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.ExtraGameArgs, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.ExtraGameArgs = normalized;
            OnPropertyChanged();
        }
    }

    public string ConfigDirectory => _stack.Config.ConfigDirectory;

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
            if (SetProperty(ref _isBusy, value))
            {
                _detectJavaCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand SaveCommand { get; }

    public ICommand BrowseGameFolderCommand { get; }

    public ICommand BrowseJavaCommand { get; }

    public ICommand BrowseBackgroundFileCommand { get; }

    public ICommand BrowseBackgroundFolderCommand { get; }

    public ICommand OpenBackgroundFolderCommand { get; }

    public ICommand DetectJavaCommand { get; }

    // ---------------------------------------------------------------- 全局操作

    private void Save()
    {
        _stack.Config.Save();
        App.RefreshBackground();
        StatusText = $"已保存到 {_stack.Config.ConfigDirectory}";
        _stack.Log.Info("设置已保存");
        _stack.RaiseChanged();
    }

    private void BrowseGameFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择游戏目录（.minecraft）",
            Multiselect = false,
        };

        if (Directory.Exists(GameFolder))
        {
            dialog.InitialDirectory = GameFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            GameFolder = dialog.FolderName;
            _stack.Config.Save();
        }
    }

    private void BrowseJava()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java 可执行文件",
            Filter = "java 可执行文件|java.exe;javaw.exe|所有文件|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            JavaPath = dialog.FileName;
            _stack.Config.Save();
        }
    }

    private void BrowseBackgroundFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            BackgroundCustomPath = dialog.FileName;
            SetBackgroundMode(BackgroundMode.Custom);
        }
    }

    private void BrowseBackgroundFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择背景图片目录",
            Multiselect = false,
        };

        if (dialog.ShowDialog() == true)
        {
            BackgroundCustomPath = dialog.FolderName;
            SetBackgroundMode(BackgroundMode.Custom);
        }
    }

    private void OpenBackgroundFolder()
    {
        BackgroundPicker.EnsureFolder(AppContext.BaseDirectory, _stack.Log);
        var folder = BackgroundPicker.FolderPath(AppContext.BaseDirectory);

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _stack.Log.Warn($"打开背景图目录失败：{folder}", ex);
            StatusText = "打开 bgskin 目录失败：" + ex.Message;
        }
    }

    private async Task DetectJavaAsync()
    {
        IsBusy = true;
        StatusText = "正在探测本机 Java（可能需要十几秒）…";

        try
        {
            var detected = await Task.Run(() => _stack.Java.Detect(forceRefresh: true));

            Javas.Clear();
            _stack.Config.Settings.DetectedJavas.Clear();

            foreach (var java in detected)
            {
                var item = new DetectedJava
                {
                    Path = java.Path,
                    RawVersion = java.RawVersion,
                    MajorVersion = java.MajorVersion,
                    Is64Bit = java.Is64Bit,
                    Vendor = java.Vendor,
                };

                Javas.Add(item);
                _stack.Config.Settings.DetectedJavas.Add(item);
            }

            _stack.Config.Save();
            StatusText = detected.Count == 0
                ? "没有探测到可用的 64 位 Java，请手动指定路径。"
                : $"探测到 {detected.Count} 个可用 Java。";
        }
        catch (Exception ex)
        {
            _stack.Log.Error("探测 Java 失败", ex);
            StatusText = "探测失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
