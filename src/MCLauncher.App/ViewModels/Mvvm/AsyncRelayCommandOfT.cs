using System.Windows.Input;

namespace MCLauncher.App.ViewModels.Mvvm;

/// <summary>
/// 带参数的异步命令（非泛型版在同目录的 RelayCommand.cs 里）。
/// 执行期间自动禁止重复触发，避免用户连点两次装两遍游戏。
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter) =>
        !_isRunning && (_canExecute?.Invoke(Cast(parameter)) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(Cast(parameter)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 用户主动取消，不算错误。
        }
        catch (Exception ex)
        {
            // 命令内部应自行处理异常；这里兜底避免 async void 把进程带崩。
            System.Diagnostics.Debug.WriteLine($"异步命令异常：{ex}");
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Cast(object? parameter) => parameter is T value ? value : default;
}
