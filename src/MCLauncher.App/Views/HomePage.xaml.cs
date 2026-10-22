using System.Windows.Controls;

namespace MCLauncher.App.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    /// <summary>启动输出逐行追加时自动滚到底部，始终显示最新一行。</summary>
    private void OnLaunchOutputChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.ScrollToEnd();
        }
    }
}
