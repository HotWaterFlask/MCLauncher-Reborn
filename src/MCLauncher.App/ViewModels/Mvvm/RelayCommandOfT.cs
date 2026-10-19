using System.Windows.Input;

namespace MCLauncher.App.ViewModels.Mvvm;

/// <summary>泛型同步命令，省掉到处写装箱/拆箱。</summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(Cast(parameter)) ?? true;

    public void Execute(object? parameter) => _execute(Cast(parameter));

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Cast(object? parameter) => parameter is T typed ? typed : default;
}
