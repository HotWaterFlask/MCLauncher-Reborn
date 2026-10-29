using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>启动公告与版本检查的解析与失败降级。</summary>
public sealed class LauncherInfoServiceTests
{
    private const string NoticeJson = """
    {"id":"n1","title":"维护公告","message":"今晚 23:00 维护，请提前退出游戏","url":"https://example.com/notice"}
    """;

    private const string ReleaseJson = """
    {"tag_name":"v6.1.0","name":"6.1.0 更新","body":"改进启动速度\n修复若干问题",
     "html_url":"https://example.com/release","published_at":"2026-10-20T10:00:00Z"}
    """;

    private static LauncherInfoService Create(string content, out TestLog log, out HttpDownloader downloader)
    {
        log = new TestLog();
        downloader = new HttpDownloader(log, new FakeHttpHandler(content));
        return new LauncherInfoService(downloader, log);
    }

    [Fact]
    public async Task 解析公告字段()
    {
        var service = Create(NoticeJson, out _, out var downloader);
        using (downloader)
        {
            var notice = await service.FetchNoticeAsync();

            Assert.NotNull(notice);
            Assert.Equal("n1", notice!.Id);
            Assert.Equal("维护公告", notice.Title);
            Assert.Equal("今晚 23:00 维护，请提前退出游戏", notice.Message);
            Assert.Equal("https://example.com/notice", notice.Url);
        }
    }

    [Fact]
    public async Task 公告可以标记为停用()
    {
        var service = Create("""{"enabled":false,"id":"n1","title":"x"}""", out _, out var downloader);
        using (downloader)
        {
            Assert.Null(await service.FetchNoticeAsync());
        }
    }

    [Fact]
    public async Task 解析更新版本()
    {
        var service = Create(ReleaseJson, out _, out var downloader);
        using (downloader)
        {
            var update = await service.CheckUpdateAsync();

            Assert.NotNull(update);
            Assert.Equal("v6.1.0", update!.Version);
            Assert.Equal("6.1.0 更新", update.Title);
            Assert.Equal("https://example.com/release", update.Url);
            Assert.NotNull(update.PublishedAt);
        }
    }

    [Fact]
    public async Task 无效内容不会抛异常()
    {
        var service = Create("<html>not json</html>", out var log, out var downloader);
        using (downloader)
        {
            Assert.Null(await service.FetchNoticeAsync());
            Assert.Null(await service.CheckUpdateAsync());
            Assert.True(log.Contains("获取启动器公告失败"));
            Assert.True(log.Contains("检查启动器更新失败"));
        }
    }

    [Theory]
    [InlineData("v6.1.0", "6.0.0", true)]
    [InlineData("6.0.0", "6.0.0", false)]
    [InlineData("6.0.0.1", "6.0.0", true)]
    [InlineData("v5.9.9", "6.0.0", false)]
    [InlineData("v6.1.0-beta", "6.0.0", true)]
    [InlineData("not-a-version", "6.0.0", false)]
    public void 版本号比较(string latest, string current, bool expected)
    {
        Assert.Equal(expected, LauncherInfoService.IsNewerVersion(latest, current));
    }
}
