using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

namespace MCLauncher.App.ViewModels;

/// <summary>
/// 主页：原版右下角控件区的外观保持不变，但按钮已接上真实功能。
/// 「启动游戏」= 补全缺失文件 → 选 Java → 拼参数 → 起进程。
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly LauncherStack _stack;
    private readonly AsyncRelayCommand _launchCommand;
    private readonly AsyncRelayCommand _completeCommand;

    private InstalledVersionInfo? _selectedVersion;
    private string _statusText = "就绪";
    private double _progress;
    private bool _isBusy;
    private string _accountText = "未选择账号";

    public HomeViewModel(LauncherStack stack, Action openSettings)
    {
        _stack = stack;

        OpenFolderCommand = new RelayCommand(OpenGameFolder);
        SettingsCommand = new RelayCommand(openSettings);
        RefreshCommand = new RelayCommand(Refresh);

        _launchCommand = new AsyncRelayCommand(LaunchAsync, () => !IsBusy && SelectedVersion is not null);
        _completeCommand = new AsyncRelayCommand(CompleteAsync, () => !IsBusy && SelectedVersion is not null);
        LaunchCommand = _launchCommand;
        CompleteCommand = _completeCommand;

        _stack.Changed += (_, _) => UiThread.Post(Refresh);
        Refresh();
    }

    public ObservableCollection<InstalledVersionInfo> Versions { get; } = new();

    public InstalledVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value))
            {
                return;
            }

            _stack.Config.Settings.SelectedInstanceId = value?.Id;
            _launchCommand.RaiseCanExecuteChanged();
            _completeCommand.RaiseCanExecuteChanged();

            if (value is not null)
            {
                StatusText = $"已选择 {value.Display}";
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string AccountText
    {
        get => _accountText;
        private set => SetProperty(ref _accountText, value);
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

            _launchCommand.RaiseCanExecuteChanged();
            _completeCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand OpenFolderCommand { get; }

    public ICommand LaunchCommand { get; }

    public ICommand CompleteCommand { get; }

    public ICommand SettingsCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>重新扫描本地版本并尽量保持当前选中项。</summary>
    public void Refresh()
    {
        var previous = SelectedVersion?.Id ?? _stack.Config.Settings.SelectedInstanceId;

        Versions.Clear();
        foreach (var version in _stack.GetInstalledVersions())
        {
            Versions.Add(version);
        }

        SelectedVersion = Versions.FirstOrDefault(v => v.Id == previous) ?? Versions.FirstOrDefault();

        var account = _stack.Accounts.Selected;
        AccountText = account is null ? "未选择账号（启动时会自动创建离线账号）" : $"账号：{account}";

        if (Versions.Count == 0)
        {
            StatusText = "本地还没有版本，请到「版本」页下载安装";
        }
    }

    private LaunchOptions BuildLaunchOptions()
    {
        var settings = _stack.Config.Settings;
        return new LaunchOptions
        {
            MaxMemoryMb = settings.MaxMemoryMb,
            MinMemoryMb = settings.MinMemoryMb,
            Width = settings.DefaultWidth,
            Height = settings.DefaultHeight,
            ExtraJvmArgs = settings.ExtraJvmArgs,
            ExtraGameArgs = settings.ExtraGameArgs,
            CompleteBeforeLaunch = true,
            LauncherName = "MCLauncher-Reborn",
            LauncherVersion = "6.0",
        };
    }

    private async Task LaunchAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = $"正在准备启动 {version.Display} …";
        _stack.LaunchLog.Clear();

        GameProcess? process = null;

        try
        {
            var account = _stack.Accounts.EnsureUsable();
            _stack.Config.Save();

            var request = new GameLaunchRequest
            {
                GameRoot = _stack.GameRoot,
                VersionId = version.Id,
                Account = account,
                Options = BuildLaunchOptions(),
                Isolated = _stack.IsIsolated(version.Id),
            };

            process = await _stack.LaunchService.LaunchAsync(
                request,
                _stack.LaunchLog,
                new Progress<InstallProgress>(OnInstallProgress));

            Progress = 1;
            StatusText = $"游戏已启动（PID {process.Id}）";
        }
        catch (Exception ex)
        {
            _stack.Log.Error($"启动 {version.Id} 失败", ex);
            StatusText = "启动失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (process is not null)
        {
            _ = WatchExitAsync(process);
        }
    }

    private async Task CompleteAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;

        try
        {
            var report = await _stack.CompleteVersionAsync(
                version.Id,
                new Progress<InstallProgress>(OnInstallProgress));

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

    private void OnInstallProgress(InstallProgress progress)
    {
        // Progress<T> 已经把回调 marshal 到 UI 线程。
        Progress = progress.Fraction;
        StatusText = progress.Total > 0
            ? $"{progress.Stage} {progress.Completed}/{progress.Total}"
            : progress.Stage;
    }

    private async Task WatchExitAsync(GameProcess process)
    {
        try
        {
            var exitCode = await process.WaitForExitAsync();
            _stack.Log.Info($"游戏进程已退出：{exitCode}");
            UiThread.Post(() => StatusText = GameLauncher.DescribeExitCode(exitCode));
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("等待游戏进程退出时出错", ex);
        }
        finally
        {
            process.Dispose();
        }
    }

    private void OpenGameFolder()
    {
        var folder = _stack.GameRoot;
        Directory.CreateDirectory(folder);

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            StatusText = $"已打开 {folder}";
        }
        catch (Exception ex)
        {
            StatusText = "打开目录失败：" + ex.Message;
        }
    }
}
