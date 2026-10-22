using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MCLauncher.App.ViewModels;

namespace MCLauncher.App.Views;

public partial class AccountsPage : UserControl
{
    public AccountsPage()
    {
        InitializeComponent();
    }

    /// <summary>双击整行把该账号设为当前账号。</summary>
    private void OnAccountRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: AccountItem item } &&
            DataContext is AccountsViewModel viewModel)
        {
            e.Handled = true;

            // 设当前账号会重建列表，等这次双击事件结束后再执行。
            Dispatcher.BeginInvoke(() => viewModel.SetCurrent(item));
        }
    }

    /// <summary>换肤：文件选择框放在代码后置，选完交给 ViewModel 上传。</summary>
    private async void OnChangeSkinClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountsViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Minecraft 皮肤",
            Filter = "皮肤图片 (*.png)|*.png",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.ApplySkinFileAsync(dialog.FileName);
        }
    }

    /// <summary>外置登录：密码从 PasswordBox 直接取，不经过数据绑定。</summary>
    private async void OnExternalLoginClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountsViewModel viewModel)
        {
            return;
        }

        var password = ExternalPasswordBox.Password;
        ExternalPasswordBox.Password = string.Empty;
        await viewModel.LoginExternalAsync(password);
    }
}
