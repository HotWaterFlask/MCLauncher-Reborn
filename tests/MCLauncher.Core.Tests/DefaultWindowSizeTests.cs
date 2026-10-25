using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

/// <summary>首次使用时的默认游戏窗口大小：常见分辨率走固定档位，非常规分辨率自动缩放。</summary>
public class DefaultWindowSizeTests
{
    [Theory]
    [InlineData(1280, 720, 850, 480)]     // 720p
    [InlineData(1366, 768, 850, 480)]     // 720p 笔记本常见分辨率
    [InlineData(1920, 1080, 1280, 720)]   // 1080p
    [InlineData(2560, 1440, 1920, 1080)]  // 2K
    [InlineData(2560, 1600, 1920, 1080)]  // 2.5K
    [InlineData(3440, 1440, 1920, 1080)]  // 带鱼屏
    [InlineData(3840, 2160, 2560, 1440)]  // 4K
    [InlineData(7680, 4320, 3840, 2160)]  // 8K
    public void 常见分辨率使用固定档位(int screenWidth, int screenHeight, int width, int height)
    {
        Assert.Equal((width, height), DefaultWindowSize.Choose(screenWidth, screenHeight));
    }

    [Fact]
    public void 小屏自动缩放到屏幕的八成左右()
    {
        var (width, height) = DefaultWindowSize.Choose(1024, 600);

        Assert.Equal(854, width);
        Assert.Equal(480, height);
        Assert.True(width <= 1024 * 0.9);
        Assert.True(height <= 600 * 0.85);
    }

    [Fact]
    public void 纵向屏不会横向超出屏幕()
    {
        var (width, height) = DefaultWindowSize.Choose(1080, 1920);

        Assert.True(width <= 1080 * 0.9);
        Assert.True(height <= 1920 * 0.85);
        Assert.True(width >= DefaultWindowSize.FallbackWidth);
    }

    [Fact]
    public void 取不到分辨率时退回854x480()
    {
        Assert.Equal((854, 480), DefaultWindowSize.Choose(0, 0));
    }
}
