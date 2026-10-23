using System.Windows;
using System.Windows.Controls;
using MCLauncher.App.ViewModels;

namespace MCLauncher.App.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
    }

    /// <summary>外置登录：密码从 PasswordBox 直接取，不经过数据绑定。</summary>
    private async void OnExternalLoginClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel viewModel)
        {
            return;
        }

        var password = ExternalPasswordBox.Password;
        ExternalPasswordBox.Password = string.Empty;
        await viewModel.LoginExternalAsync(password);
    }
}
