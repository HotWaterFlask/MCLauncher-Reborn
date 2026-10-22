using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MCLauncher.App.ViewModels;

namespace MCLauncher.App.Views;

public partial class VersionsPage : UserControl
{
    public VersionsPage()
    {
        InitializeComponent();
    }

    /// <summary>双击实例直接进入实例管理（模组 / 资源包 / 世界等）。</summary>
    private void OnInstanceDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击「启动 / ⋯」等按钮时不要顺手进管理页。
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (DataContext is VersionsViewModel { SelectedInstance: { } row })
        {
            row.Owner.ManageCommand.Execute(row);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>「⋯」按钮：左键直接把右键菜单贴在按钮下方弹出。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
