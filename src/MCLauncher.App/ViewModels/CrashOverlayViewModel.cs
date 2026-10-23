using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;

namespace MCLauncher.App.ViewModels;

/// <summary>
/// 全局崩溃界面：游戏异常退出后盖在整个窗口上，
/// 给出可能原因、可复制的日志，以及打开报告 / 打开实例目录 / 重新启动等操作。
/// </summary>
public sealed class CrashOverlayViewModel : ViewModelBase
{
    private readonly LauncherStack _stack;
    private readonly RelayCommand _openReportCommand;
    private readonly RelayCommand _openFolderCommand;
    private readonly RelayCommand _copyLogCommand;
    private readonly RelayCommand _restartCommand;

    private CrashReport? _report;
    private string _instanceDirectory = string.Empty;
    private string _title = "游戏崩溃了";
    private string _subtitle = string.Empty;
    private string _summary = string.Empty;
    private string _logText = string.Empty;
    private string _hint = string.Empty;
    private bool _isOpen;

    public CrashOverlayViewModel(LauncherStack stack)
    {
        _stack = stack;

        _openReportCommand = new RelayCommand(OpenReport, () => _report?.ReportPath is not null);
        _openFolderCommand = new RelayCommand(OpenFolder, () => _instanceDirectory.Length > 0);
        _copyLogCommand = new RelayCommand(CopyLog, () => LogText.Length > 0);
        _restartCommand = new RelayCommand(Restart, () => RestartAction is not null);

        OpenReportCommand = _openReportCommand;
        OpenFolderCommand = _openFolderCommand;
        CopyLogCommand = _copyLogCommand;
        RestartCommand = _restartCommand;
        CloseCommand = new RelayCommand(Close);
    }

    /// <summary>由外壳注入：回到首页并用当前实例重新启动。</summary>
    public Action? RestartAction { get; set; }

    public ObservableCollection<CrashCause> Causes { get; } = new();

    public ICommand OpenReportCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand CopyLogCommand { get; }

    public ICommand RestartCommand { get; }

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

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
            {
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    }

    public bool HasSummary => Summary.Length > 0;

    public string LogText
    {
        get => _logText;
        private set
        {
            if (SetProperty(ref _logText, value))
            {
                _copyLogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Hint
    {
        get => _hint;
        private set => SetProperty(ref _hint, value);
    }

    /// <summary>显示崩溃界面（可从后台线程调用）。</summary>
    public void Show(GameCrashedEventArgs args) =>
        UiThread.Post(() => Apply(args));

    private void Apply(GameCrashedEventArgs args)
    {
        _report = args.Report;
        _instanceDirectory = args.InstanceDirectory;

        Title = args.Report.Title;
        Subtitle = $"{args.InstanceName} · 退出码 {args.Report.ExitCode}";
        Summary = string.IsNullOrWhiteSpace(args.Report.Description)
            ? string.Empty
            : "错误：" + args.Report.Description;

        LogText = args.Report.LogText;
        Hint = string.Empty;

        Causes.Clear();
        foreach (var cause in args.Report.Causes)
        {
            Causes.Add(cause);
        }

        IsOpen = true;

        _openReportCommand.RaiseCanExecuteChanged();
        _openFolderCommand.RaiseCanExecuteChanged();
        _restartCommand.RaiseCanExecuteChanged();
    }

    private void OpenReport()
    {
        var path = _report?.ReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Hint = "找不到崩溃报告文件，可能已被删除。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Hint = "已用默认程序打开崩溃报告。";
        }
        catch (Exception ex)
        {
            _stack.Log.Warn($"打开崩溃报告失败：{path}", ex);
            Hint = "打开崩溃报告失败：" + ex.Message;
        }
    }

    private void OpenFolder()
    {
        var folder = _instanceDirectory;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _stack.Log.Warn($"打开实例目录失败：{folder}", ex);
            Hint = "打开实例目录失败：" + ex.Message;
        }
    }

    private void CopyLog()
    {
        if (LogText.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(LogText);
            Hint = "日志已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("复制崩溃日志失败", ex);
            Hint = "复制失败：" + ex.Message;
        }
    }

    private void Restart()
    {
        var action = RestartAction;
        if (action is null)
        {
            return;
        }

        Close();
        action();
    }

    private void Close() => IsOpen = false;
}
