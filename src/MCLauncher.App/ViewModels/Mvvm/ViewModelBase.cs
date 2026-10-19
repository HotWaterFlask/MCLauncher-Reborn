using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MCLauncher.App.ViewModels.Mvvm;

/// <summary>所有 ViewModel 的基类：属性变更通知 + 批量抑制。</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    private int _suppressDepth;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_suppressDepth > 0 || propertyName is null)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>批量更新多个属性时抑制通知，结束时只发一次。</summary>
    protected IDisposable SuppressNotifications()
    {
        _suppressDepth++;
        return new SuppressScope(this);
    }

    private sealed class SuppressScope : IDisposable
    {
        private readonly ViewModelBase _owner;
        private bool _disposed;

        public SuppressScope(ViewModelBase owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner._suppressDepth--;
            if (_owner._suppressDepth == 0)
            {
                _owner.PropertyChanged?.Invoke(_owner, new PropertyChangedEventArgs(string.Empty));
            }
        }
    }
}
