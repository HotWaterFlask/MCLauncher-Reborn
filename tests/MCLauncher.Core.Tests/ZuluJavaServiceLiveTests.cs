using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>Azul Metadata API 的联网用例；不下载完整的 JDK 包。</summary>
public sealed class ZuluJavaServiceLiveTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(21)]
    public async Task Live_解析指定主版本的Zulu安装包(int major)
    {
        using var temp = new TempDirectory();
        using var stack = LiveTestStack.Create(temp.Path);

        var package = await stack.ZuluJava.ResolveAsync(major);

        Assert.StartsWith($"zulu{major}", package.Name, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".zip", package.DownloadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(major, package.JavaVersion[0]);
        Assert.True(package.Size > 10_000_000, $"安装包体积异常：{package.Size}");
        Assert.Equal(64, package.Sha256.Length);
    }
}
