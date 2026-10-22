using System.Windows.Media;

namespace MCLauncher.App.Themes;

/// <summary>皮肤相关的常量与画刷，供代码与 XAML 共用。</summary>
public static class Skin
{
    /// <summary>窗口圆角半径。</summary>
    public const double CornerRadius = 8;

    /// <summary>默认背景图资源路径（整窗铺满）。</summary>
    public static readonly Uri BackgroundImage = new("pack://application:,,,/Assets/default-bg.jpg");

    /// <summary>内容区半透明底板。</summary>
    public static readonly SolidColorBrush ContentPanel = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));

    /// <summary>导航栏半透明深色底。</summary>
    public static readonly SolidColorBrush NavPanel = Frozen(Color.FromArgb(0x59, 0x00, 0x00, 0x00));

    /// <summary>浅灰按钮渐变（原版按钮观感）。</summary>
    public static LinearGradientBrush ButtonBackground { get; } = CreateButtonBrush();

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateButtonBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFC, 0xFC, 0xFC), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xED, 0xED, 0xED), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xDE, 0xDE, 0xDE), 1));
        brush.Freeze();
        return brush;
    }
}
