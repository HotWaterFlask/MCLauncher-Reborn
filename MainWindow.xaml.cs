using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MCLauncher_reborn
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string ProjectUrl = "https://github.com/HotWaterFlask/MCLauncher-Reborn";

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>关闭窗口（原版右上角的 ×）。</summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>点击左下角署名，用默认浏览器打开项目主页。</summary>
        private void OnCreditClick(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }

        /// <summary>窗口没有标题栏，按住背景即可拖动。</summary>
        private void OnDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}