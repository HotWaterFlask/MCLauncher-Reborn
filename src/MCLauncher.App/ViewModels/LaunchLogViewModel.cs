using System.Text;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;

namespace MCLauncher.App.ViewModels;

/// <summary>启动日志页：镜像游戏进程的标准输出/错误输出。</summary>
public sealed class LaunchLogViewModel : ViewModelBase
{
    private const int MaxLines = 3000;

    private readonly LauncherStack _stack;
    private readonly Queue<string> _lines = new();
    private readonly object _gate = new();
    private string _logText = string.Empty;

    public LaunchLogViewModel(LauncherStack stack)
    {
        _stack = stack;

        ClearCommand = new RelayCommand(() =>
        {
            lock (_gate)
            {
                _lines.Clear();
            }

            LogText = string.Empty;
            _stack.LaunchLog.Clear();
        });

        // 游戏进程的读取线程是后台线程，必须 marshal 回 UI。
        _stack.LaunchLog.LineAdded += line => UiThread.Post(() => Append(line));
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public ICommand ClearCommand { get; }

    private void Append(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }
        }

        var builder = new StringBuilder();
        lock (_gate)
        {
            foreach (var item in _lines)
            {
                builder.AppendLine(item);
            }
        }

        LogText = builder.ToString();
    }
}
