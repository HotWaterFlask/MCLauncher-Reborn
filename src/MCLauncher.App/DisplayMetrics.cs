using System.Runtime.InteropServices;

namespace MCLauncher.App;

/// <summary>
/// 主显示器的物理像素尺寸。启动器是 DPI 感知的，取到的就是真实分辨率，
/// 不会因为系统缩放（如 150%）被换算成小一号的数值。
/// </summary>
internal static class DisplayMetrics
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    /// <summary>主显示器宽高；取不到时返回 (0, 0)。</summary>
    public static (int Width, int Height) PrimaryScreenSize()
    {
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        return width > 0 && height > 0 ? (width, height) : (0, 0);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
