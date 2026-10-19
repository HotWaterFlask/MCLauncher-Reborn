using System.Text;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Network;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 用假 HTTP 处理器确定性地覆盖下载器的各条分支：断点续传、服务器忽略 Range、
/// 失败重试、哈希校验、进度上报。走官方域名 + 假处理器 → 不触发镜像节流，测试跑得快。
/// </summary>
public sealed class HttpDownloaderTests
{
    private const string Url = "https://libraries.minecraft.net/file.bin";
    private const string Content = "hello-minecraft-reborn-downloader-test-content";

    private static IReadOnlyList<DownloadCandidate> Candidates(params string[] urls) =>
        urls.Select(u => new DownloadCandidate(u, "测试源")).ToArray();

    private static FileChecker Checker() =>
        FileChecker.Of(HashUtil.Sha1OfString(Content), Encoding.UTF8.GetByteCount(Content));

    [Fact]
    public async Task 正常下载并通过校验()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content);
        using var downloader = new HttpDownloader(new TestLog(), handler);

        var target = temp.File("out.bin");
        var outcome = await downloader.DownloadAsync(Candidates(Url), target, Checker());

        Assert.True(outcome.Success, outcome.Error);
        Assert.False(outcome.Skipped);
        Assert.Equal(Content, File.ReadAllText(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task 已存在且校验通过时跳过下载()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content);
        using var downloader = new HttpDownloader(new TestLog(), handler);

        var target = temp.File("out.bin");
        File.WriteAllText(target, Content);

        var outcome = await downloader.DownloadAsync(Candidates(Url), target, Checker());

        Assert.True(outcome.Success);
        Assert.True(outcome.Skipped);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task 带part分片时走206断点续传()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content);
        using var downloader = new HttpDownloader(new TestLog(), handler);

        var target = temp.File("out.bin");
        var bytes = Encoding.UTF8.GetBytes(Content);
        File.WriteAllBytes(target + ".part", bytes[..10]);

        var progress = new List<DownloadProgress>();
        var outcome = await downloader.DownloadAsync(Candidates(Url), target, Checker(), new InlineProgress<DownloadProgress>(progress.Add));

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(Content, File.ReadAllText(target));
        Assert.Contains(handler.RangeHeaders, h => h == "bytes=10-");

        // 续传时进度应从已有的 10 字节起算，而不是从 0。
        Assert.True(progress.Count > 0);
        Assert.Equal(10, progress[0].BytesReceived);
        Assert.Equal(bytes.Length, progress[^1].BytesReceived);
    }

    [Fact]
    public async Task 服务器忽略Range时丢弃分片重下()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content, supportsRange: false);
        var log = new TestLog();
        using var downloader = new HttpDownloader(log, handler);

        var target = temp.File("out.bin");
        File.WriteAllBytes(target + ".part", Encoding.UTF8.GetBytes(Content)[..10]);

        var outcome = await downloader.DownloadAsync(Candidates(Url), target, Checker());

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(Content, File.ReadAllText(target));
        Assert.True(log.Contains("不支持断点续传"), "应记录服务器不支持断点续传");
    }

    [Fact]
    public async Task 哈希不符时判失败并清掉残留文件()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content);
        using var downloader = new HttpDownloader(new TestLog(), handler);

        var target = temp.File("out.bin");
        var wrong = FileChecker.Of(new string('a', 40), Encoding.UTF8.GetByteCount(Content));

        var outcome = await downloader.DownloadAsync(Candidates(Url), target, wrong);

        Assert.False(outcome.Success);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task 同一候选失败后会重试()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content, failFirstRequests: 2);
        var log = new TestLog();
        using var downloader = new HttpDownloader(log, handler);

        var target = temp.File("out.bin");
        var outcome = await downloader.DownloadAsync(Candidates(Url), target, Checker());

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(3, outcome.Attempts);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task 前置候选全挂时切换到后面的候选()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content, failFirstRequests: 3);
        var log = new TestLog();
        using var downloader = new HttpDownloader(log, handler);

        var target = temp.File("out.bin");
        var outcome = await downloader.DownloadAsync(
            Candidates("https://libraries.minecraft.net/bad.bin", "https://libraries.minecraft.net/good.bin"),
            target,
            Checker());

        Assert.True(outcome.Success, outcome.Error);
        Assert.False(log.Contains("所有候选"), "不应该直接放弃");
    }

    [Fact]
    public async Task 所有候选都失败时返回失败()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content, failFirstRequests: 1000);
        using var downloader = new HttpDownloader(new TestLog(), handler);

        var target = temp.File("out.bin");
        var outcome = await downloader.DownloadAsync(
            Candidates("https://libraries.minecraft.net/a.bin", "https://libraries.minecraft.net/b.bin"),
            target,
            Checker());

        Assert.False(outcome.Success);
        Assert.Equal(6, outcome.Attempts); // 2 个候选 × 3 次尝试
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task 获取文本走候选列表()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content);
        using var downloader = new HttpDownloader(new TestLog(), handler);

        var text = await downloader.GetStringAsync(Candidates(Url));
        Assert.Equal(Content, text);
    }

    [Fact]
    public async Task 批量下载汇总进度()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content);
        using var downloader = new HttpDownloader(new TestLog(), handler);
        var batch = new DownloadBatch(downloader, new TestLog());

        var requests = Enumerable.Range(0, 4).Select(i => new DownloadRequest
        {
            Name = $"file{i}.bin",
            Candidates = Candidates(Url),
            TargetPath = temp.File($"file{i}.bin"),
            Checker = Checker(),
        }).ToList();

        DownloadBatchProgress? last = null;
        var result = await batch.RunAsync(requests, maxParallel: 4, new InlineProgress<DownloadBatchProgress>(p => last = p));

        Assert.True(result.Success);
        Assert.Equal(4, result.Completed);
        Assert.Equal(0, result.Failed);
        Assert.NotNull(last);
        Assert.Equal(4, last!.Total);
        Assert.Equal(4, last.Completed);
        Assert.Equal(Encoding.UTF8.GetByteCount(Content) * 4, last.DownloadedBytes);
    }

    [Fact]
    public async Task 批量下载中可选文件失败不算整批失败()
    {
        using var temp = new TempDirectory();
        var handler = new FakeHttpHandler(Content, failFirstRequests: 1000);
        using var downloader = new HttpDownloader(new TestLog(), handler);
        var batch = new DownloadBatch(downloader, new TestLog());

        var requests = new List<DownloadRequest>
        {
            new()
            {
                Name = "opt.bin",
                Candidates = Candidates(Url),
                TargetPath = temp.File("opt.bin"),
                Checker = Checker(),
                Optional = true,
            },
        };

        var result = await batch.RunAsync(requests);
        Assert.True(result.Success);
        Assert.Equal(1, result.Skipped);
    }
}
