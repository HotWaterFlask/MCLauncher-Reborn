using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

namespace MCLauncher.App.ViewModels;

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

    public bool PreferMirror
    {
        get => _stack.Config.Settings.PreferMirror;
        set
        {
            if (_stack.Config.Settings.PreferMirror == value)
            {
                return;
            }

            _stack.Config.Settings.PreferMirror = value;
            _stack.Resolver.PreferMirror = value;
            OnPropertyChanged();
        }
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

    public ICommand DetectJavaCommand { get; }

    // ---------------------------------------------------------------- 全局操作

    private void Save()
    {
        _stack.Config.Save();
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
