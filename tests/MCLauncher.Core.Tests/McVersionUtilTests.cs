using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class McVersionUtilTests
{
    [Theory]
    [InlineData("1.20.4", true)]
    [InlineData("1.7.10", true)]
    [InlineData("1.0", true)]
    [InlineData("1.20.4-forge-49.0.30", false)]
    [InlineData("23w45a", false)]
    [InlineData("b1.7.3", false)]
    public void 识别纯净正式版号(string id, bool expected) =>
        Assert.Equal(expected, McVersionUtil.IsPlainRelease(id));

    [Fact]
    public void 解析正式版号()
    {
        Assert.True(McVersionUtil.TryParseRelease("1.20.4", out var major, out var minor, out var patch));
        Assert.Equal((1, 20, 4), (major, minor, patch));

        Assert.True(McVersionUtil.TryParseRelease("1.7", out major, out minor, out patch));
        Assert.Equal((1, 7, 0), (major, minor, patch));
    }

    [Fact]
    public void 带加载器后缀也能解析出基准版本()
    {
        Assert.Equal("1.20.4", McVersionUtil.GetBaseVersion("1.20.4-forge-49.0.30"));
        Assert.Equal("1.20.1", McVersionUtil.GetBaseVersion("1.20.1-fabric0.15.3"));
        Assert.Equal("1.12.2", McVersionUtil.GetBaseVersion("1.12.2-OptiFine_HD_U_G5"));
        Assert.Equal("b1.7.3", McVersionUtil.GetBaseVersion("b1.7.3"));
    }

    [Fact]
    public void 识别快照与远古版本()
    {
        Assert.True(McVersionUtil.IsSnapshot("23w45a"));
        Assert.False(McVersionUtil.IsSnapshot("1.20.4"));

        Assert.True(McVersionUtil.IsAncient("b1.7.3"));
        Assert.True(McVersionUtil.IsAncient("a1.2.6"));
        Assert.False(McVersionUtil.IsAncient("1.20.4"));
    }

    [Fact]
    public void 版本号比较()
    {
        Assert.True(McVersionUtil.IsAtMost("1.7.10", 1, 7, 10));
        Assert.True(McVersionUtil.IsAtMost("1.6.4", 1, 7, 10));
        Assert.False(McVersionUtil.IsAtMost("1.8.9", 1, 7, 10));

        Assert.True(McVersionUtil.IsAtLeast("1.20.4", 1, 20, 0));
        Assert.False(McVersionUtil.IsAtLeast("1.19.4", 1, 20, 0));

        Assert.True(McVersionUtil.Compare("1.20.4", "1.20.1") > 0);
        Assert.True(McVersionUtil.Compare("1.7.10", "1.8") < 0);
    }

    [Fact]
    public void 加载器版本与原版基准号比较一致()
    {
        Assert.Equal(0, McVersionUtil.Compare("1.20.4-forge-49.0.30", "1.20.4"));
    }
}
