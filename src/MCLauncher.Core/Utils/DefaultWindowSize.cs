namespace MCLauncher.Core.Utils;

/// <summary>
/// 首次使用时按主显示器分辨率挑选游戏窗口的默认大小。
/// 常见分辨率走固定档位；纵向屏、超小屏这类非常规分辨率则按屏幕比例自动缩放，
/// 避免窗口顶满屏幕或者小得没法用。
/// </summary>
public static class DefaultWindowSize
{
    /// <summary>取不到显示器分辨率时的兜底尺寸。</summary>
    public const int FallbackWidth = 854;

    public const int FallbackHeight = 480;

    public static (int Width, int Height) Choose(int screenWidth, int screenHeight)
    {
        if (screenWidth < 320 || screenHeight < 240)
        {
            return (FallbackWidth, FallbackHeight);
        }

        if (PresetForHeight(screenHeight) is { } preset && Fits(preset.Width, preset.Height, screenWidth, screenHeight))
        {
            return preset;
        }

        return Fit(screenWidth, screenHeight);
    }

    /// <summary>按屏幕高度归类：720p / 1080p / 2K / 4K / 8K。</summary>
    private static (int Width, int Height)? PresetForHeight(int screenHeight) => screenHeight switch
    {
        >= 4320 => (3840, 2160),
        >= 2160 => (2560, 1440),
        >= 1440 => (1920, 1080),
        >= 900 => (1280, 720),
        >= 700 => (850, 480),
        _ => null,
    };

    /// <summary>窗口不要顶满屏幕：宽度留一成，高度给任务栏留一成半。</summary>
    private static bool Fits(int width, int height, int screenWidth, int screenHeight) =>
        width <= screenWidth * 0.9 && height <= screenHeight * 0.85;

    /// <summary>非常规分辨率：取屏幕 85% 宽、80% 高以内的最大 16:9 窗口，并保证不小于 854x480（屏幕装得下时）。</summary>
    private static (int Width, int Height) Fit(int screenWidth, int screenHeight)
    {
        var maxWidth = Math.Max(320, (int)(screenWidth * 0.85));
        var maxHeight = Math.Max(240, (int)(screenHeight * 0.80));

        var height = Math.Min(maxHeight, maxWidth * 9 / 16);
        var width = Math.Min(maxWidth, height * 16 / 9);

        width = Math.Max(width, Math.Min(FallbackWidth, maxWidth));
        height = Math.Max(height, Math.Min(FallbackHeight, maxHeight));

        return (Math.Max(width, 320), Math.Max(height, 240));
    }
}
