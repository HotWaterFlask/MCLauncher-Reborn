using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MCLauncher.App.ViewModels;

namespace MCLauncher.App.Views;

public partial class ResourceDownloadView : UserControl
{
    private ResourceDownloadViewModel? _viewModel;

    public ResourceDownloadView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as ResourceDownloadViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>文件名弹窗打开时自动聚焦并全选，方便直接改名。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ResourceDownloadViewModel.IsFileNameDialogOpen)
            || _viewModel?.IsFileNameDialogOpen != true)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                FileNameBox.Focus();
                FileNameBox.SelectAll();
            },
            DispatcherPriority.Input);
    }
}
