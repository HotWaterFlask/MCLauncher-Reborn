using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;

namespace MCLauncher.App.ViewModels;

/// <summary>
/// 单个实例的独立设置：写入 <c>versions/&lt;id&gt;/.mclauncher/instance.json</c>，留空即跟随全局。
/// 由实例管理页的「游戏设置」在右侧原地展示。
/// </summary>
public sealed class InstanceSettingsViewModel : ViewModelBase
{
    private static readonly Regex WindowSizePattern =
        new(@"^\s*(\d{2,5})\s*[xX×*]\s*(\d{2,5})\s*$", RegexOptions.Compiled);

    private const string FollowGlobal = "跟随全局";
    private const string DefaultStatus = "留空表示跟随全局；点「保存实例设置」生效。";

    private readonly LauncherStack _stack;

    private string _instanceId = string.Empty;
    private InstalledVersionInfo? _instance;
    private string? _loadedInstanceId;
    private string _instanceDisplay = string.Empty;
    private string _instanceDirectory = string.Empty;
    private string _status = DefaultStatus;

    private string _displayName = string.Empty;
    private string _selectedIsolationOption = FollowGlobal;
    private string _maxMemory = string.Empty;
    private string _minMemory = string.Empty;
    private string _javaPath = string.Empty;
    private string _extraJvmArgs = string.Empty;
    private string _extraGameArgs = string.Empty;
    private string _selectedWindowMode = FollowGlobal;
    private string _windowSize = string.Empty;

    public InstanceSettingsViewModel(LauncherStack stack)
    {
        _stack = stack;

        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
        BrowseJavaCommand = new RelayCommand(BrowseJava);
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    /// <summary>实例配置保存或重置后触发，供实例管理页同步目录与显示名。</summary>
    public event EventHandler? Saved;

    // ---------------------------------------------------------------- 实例信息

    public string InstanceDisplay
    {
        get => _instanceDisplay;
        private set => SetProperty(ref _instanceDisplay, value);
    }

    public string InstanceDirectory
    {
        get => _instanceDirectory;
        private set => SetProperty(ref _instanceDirectory, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>切换到指定实例并加载它的覆盖项；同一实例重复进入时保留未保存的编辑。</summary>
    public void Show(string versionId)
    {
        var changed = !string.Equals(_instanceId, versionId, StringComparison.OrdinalIgnoreCase);

        _instanceId = versionId;
        _instance = _stack.GetInstalledVersions()
            .FirstOrDefault(version => string.Equals(version.Id, versionId, StringComparison.OrdinalIgnoreCase));

        InstanceDisplay = _instance?.Display ?? versionId;
        InstanceDirectory = _stack.GetInstanceDirectory(versionId, _stack.IsIsolated(versionId));
        OnPropertyChanged(nameof(GlobalMemoryHint));

        if (changed || _loadedInstanceId is null)
        {
            LoadFields();
        }

        Status = DefaultStatus;
    }

    // ---------------------------------------------------------------- 表单

    /// <summary>「跟随全局 / 开启 / 关闭」。</summary>
    public IReadOnlyList<string> IsolationOptions { get; } = new[] { FollowGlobal, "开启", "关闭" };

    /// <summary>「跟随全局 / 窗口化 / 全屏 / 最大化」。</summary>
    public IReadOnlyList<string> InstanceWindowModeOptions { get; } = new[]
    {
        FollowGlobal, "窗口化", "全屏", "最大化",
    };

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

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string SelectedIsolationOption
    {
        get => _selectedIsolationOption;
        set => SetProperty(ref _selectedIsolationOption, value);
    }

    /// <summary>留空表示跟随全局。</summary>
    public string MaxMemory
    {
        get => _maxMemory;
        set => SetProperty(ref _maxMemory, value);
    }

    /// <summary>留空表示跟随全局。</summary>
    public string MinMemory
    {
        get => _minMemory;
        set => SetProperty(ref _minMemory, value);
    }

    public string JavaPath
    {
        get => _javaPath;
        set => SetProperty(ref _javaPath, value);
    }

    public string ExtraJvmArgs
    {
        get => _extraJvmArgs;
        set => SetProperty(ref _extraJvmArgs, value);
    }

    public string ExtraGameArgs
    {
        get => _extraGameArgs;
        set => SetProperty(ref _extraGameArgs, value);
    }

    public string SelectedWindowMode
    {
        get => _selectedWindowMode;
        set => SetProperty(ref _selectedWindowMode, value);
    }

    /// <summary>留空跟随全局；可直接输入「宽x高」。</summary>
    public string WindowSize
    {
        get => _windowSize;
        set => SetProperty(ref _windowSize, value);
    }

    /// <summary>全局值提示，方便判断实例留空时会用多少内存。</summary>
    public string GlobalMemoryHint =>
        $"全局：最大 {_stack.Config.Settings.MaxMemoryMb} MB / 初始 {_stack.Config.Settings.MinMemoryMb} MB";

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand BrowseJavaCommand { get; }

    public ICommand OpenFolderCommand { get; }

    // ---------------------------------------------------------------- 读写配置

    private void LoadFields()
    {
        _loadedInstanceId = _instanceId;
        var config = _stack.Instances.Load(_stack.GameRoot, _instanceId);

        DisplayName = config.DisplayName ?? string.Empty;
        SelectedIsolationOption = config.Isolated switch
        {
            true => "开启",
            false => "关闭",
            _ => FollowGlobal,
        };
        MaxMemory = config.MaxMemoryMb?.ToString() ?? string.Empty;
        MinMemory = config.MinMemoryMb?.ToString() ?? string.Empty;
        JavaPath = config.JavaPath ?? string.Empty;
        ExtraJvmArgs = config.ExtraJvmArgs ?? string.Empty;
        ExtraGameArgs = config.ExtraGameArgs ?? string.Empty;
        SelectedWindowMode = config.WindowMode switch
        {
            GameWindowMode.Windowed => "窗口化",
            GameWindowMode.Fullscreen => "全屏",
            GameWindowMode.Maximized => "最大化",
            _ => FollowGlobal,
        };
        WindowSize = config.Width > 0 && config.Height > 0
            ? $"{config.Width}x{config.Height}"
            : string.Empty;

        Status = DefaultStatus;
    }

    private void Save()
    {
        if (_instanceId.Length == 0)
        {
            return;
        }

        if (!TryParseOptionalPositiveInt(MaxMemory, "最大内存", out var maxMemory) ||
            !TryParseOptionalPositiveInt(MinMemory, "初始内存", out var minMemory))
        {
            return;
        }

        if (!TryParseOptionalWindowSize(WindowSize, out var width, out var height))
        {
            return;
        }

        try
        {
            var config = _stack.Instances.Load(_stack.GameRoot, _instanceId);

            config.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim();
            config.Isolated = SelectedIsolationOption switch
            {
                "开启" => true,
                "关闭" => false,
                _ => null,
            };
            config.MaxMemoryMb = maxMemory;
            config.MinMemoryMb = minMemory;
            config.JavaPath = NormalizeOptional(JavaPath);
            config.ExtraJvmArgs = NormalizeOptional(ExtraJvmArgs);
            config.ExtraGameArgs = NormalizeOptional(ExtraGameArgs);
            config.WindowMode = SelectedWindowMode switch
            {
                "窗口化" => GameWindowMode.Windowed,
                "全屏" => GameWindowMode.Fullscreen,
                "最大化" => GameWindowMode.Maximized,
                _ => null,
            };
            config.Width = width;
            config.Height = height;

            _stack.Instances.Save(_stack.GameRoot, _instanceId, config);
            _stack.Config.Save();

            _instance = _stack.GetInstalledVersions()
                .FirstOrDefault(version => string.Equals(version.Id, _instanceId, StringComparison.OrdinalIgnoreCase));
            InstanceDisplay = _instance?.Display ?? _instanceId;
            InstanceDirectory = _stack.GetInstanceDirectory(_instanceId, _stack.IsIsolated(_instanceId));

            Status = $"实例设置已保存，下次启动 {InstanceDisplay} 时生效。";
            _stack.Log.Info($"已保存实例设置：{_instanceId}");
            _stack.RaiseChanged();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"保存实例设置失败：{_instanceId}", ex);
            Status = "保存失败：" + ex.Message;
        }
    }

    private void Reset()
    {
        if (_instanceId.Length == 0)
        {
            return;
        }

        try
        {
            var config = _stack.Instances.Load(_stack.GameRoot, _instanceId);

            config.DisplayName = null;
            config.Isolated = null;
            config.MaxMemoryMb = null;
            config.MinMemoryMb = null;
            config.JavaPath = null;
            config.ExtraJvmArgs = null;
            config.ExtraGameArgs = null;
            config.WindowMode = null;
            config.Width = null;
            config.Height = null;

            _stack.Instances.Save(_stack.GameRoot, _instanceId, config);
            _stack.Config.Save();

            _loadedInstanceId = null;
            _instance = _stack.GetInstalledVersions()
                .FirstOrDefault(version => string.Equals(version.Id, _instanceId, StringComparison.OrdinalIgnoreCase));
            InstanceDisplay = _instance?.Display ?? _instanceId;
            InstanceDirectory = _stack.GetInstanceDirectory(_instanceId, _stack.IsIsolated(_instanceId));
            LoadFields();

            Status = "已恢复默认，全部跟随全局设置。";
            _stack.RaiseChanged();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"重置实例设置失败：{_instanceId}", ex);
            Status = "重置失败：" + ex.Message;
        }
    }

    private bool TryParseOptionalPositiveInt(string text, string label, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), out var parsed) || parsed <= 0)
        {
            Status = $"{label}需要是正整数，留空表示跟随全局。";
            return false;
        }

        value = parsed;
        return true;
    }

    private bool TryParseOptionalWindowSize(string text, out int? width, out int? height)
    {
        width = null;
        height = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var match = WindowSizePattern.Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out var parsedWidth)
            || !int.TryParse(match.Groups[2].Value, out var parsedHeight)
            || parsedWidth < 100 || parsedHeight < 100)
        {
            Status = "窗口大小需要写成「宽x高」，例如 1280x720；留空表示跟随全局。";
            return false;
        }

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---------------------------------------------------------------- 辅助操作

    private void BrowseJava()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择该实例使用的 java 可执行文件",
            Filter = "java 可执行文件|java.exe;javaw.exe|所有文件|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            JavaPath = dialog.FileName;
        }
    }

    private void OpenFolder()
    {
        if (_instanceId.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(InstanceDirectory);
            Process.Start(new ProcessStartInfo { FileName = InstanceDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _stack.Log.Warn($"打开实例目录失败：{_instanceId}", ex);
            Status = "打开目录失败：" + ex.Message;
        }
    }
}
