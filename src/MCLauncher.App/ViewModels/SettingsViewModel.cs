using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Services;

namespace MCLauncher.App.ViewModels;

/// <summary>设置页：绑定 <see cref="AppSettings"/>，保存即写回配置文件。</summary>
public sealed class SettingsViewModel : ViewModelBase
{
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

    public string GameFolder
    {
        get => _stack.Config.Settings.SelectedGameFolder ?? string.Empty;
        set
        {
            if (string.Equals(_stack.Config.Settings.SelectedGameFolder, value, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.SelectedGameFolder = value;
            OnPropertyChanged();
            _stack.RaiseChanged();
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

    public string MsClientId
    {
        get => _stack.Config.Settings.MsClientId ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.MsClientId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.MsClientId = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MsClientIdHint));
        }
    }

    /// <summary>客户端 ID 缺失时的引导文案（缺失不算错误，只是功能不可用）。</summary>
    public string MsClientIdHint => _stack.Config.ResolveMsClientId() is null
        ? "尚未配置：微软正版登录将不可用。需在 Azure 注册应用后把客户端 ID 填到此处。"
        : "已配置，可用于微软正版登录。";

    public string MsSetupHint => _stack.MicrosoftAuth.DescribeSetup();

    public string CurseForgeApiKey
    {
        get => _stack.Config.Settings.CurseForgeApiKey ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_stack.Config.Settings.CurseForgeApiKey, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _stack.Config.Settings.CurseForgeApiKey = normalized;
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
