using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 用真实的官方数据验证解析层：清单 → 版本 JSON → 模型。
/// 这能挡住"按文档猜字段名"造成的隐性错误。
/// </summary>
[Trait("Category", "Live")]
public sealed class VersionManifestLiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    [Fact]
    public async Task 真实版本JSON能完整解析()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log)
        {
            VersionListSource = DownloadSourcePreference.Official,
            FileSource = DownloadSourcePreference.Official,
        };
        var service = new VersionManifestService(downloader, resolver, log, temp.Path);
        using var cts = new CancellationTokenSource(Timeout);

        var manifest = await service.GetAsync(ct: cts.Token);
        Assert.NotEmpty(manifest.Versions);

        var entry = await service.FindAsync(manifest.Latest.Release!, cts.Token);
        Assert.NotNull(entry);

        var version = await service.GetVersionJsonAsync(entry!, cts.Token);

        Assert.Equal(entry!.Id, version.Id);
        Assert.False(string.IsNullOrWhiteSpace(version.MainClass), "mainClass 不应为空");
        Assert.NotNull(version.AssetIndex);
        Assert.False(string.IsNullOrWhiteSpace(version.AssetIndex!.Url), "assetIndex.url 不应为空");
        Assert.NotNull(version.Downloads?.Client);
        Assert.False(string.IsNullOrWhiteSpace(version.Downloads!.Client!.Url), "downloads.client.url 不应为空");
        Assert.True(version.RequiredJavaMajor >= 8, "javaVersion.majorVersion 应 ≥ 8");
        Assert.NotEmpty(version.Libraries!);

        // 每个库都应该能算出相对路径（坐标解析没有漏网的写法）。
        var pathless = version.Libraries!
            .Where(l => l.Applies() && !l.IsNativeLibrary)
            .Where(l => l.ArtifactPath() is null)
            .Select(l => l.Name)
            .ToList();
        Assert.True(pathless.Count == 0, "以下库无法解析坐标：" + string.Join("；", pathless));

        // 新版版本应使用结构化参数。
        Assert.NotNull(version.Arguments);
        Assert.False(version.UsesLegacyArguments);
    }

    [Fact]
    public async Task 清单缓存落盘后二次读取不再请求网络()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log)
        {
            VersionListSource = DownloadSourcePreference.Official,
            FileSource = DownloadSourcePreference.Official,
        };
        using var cts = new CancellationTokenSource(Timeout);

        var first = new VersionManifestService(downloader, resolver, log, temp.Path);
        var manifest = await first.GetAsync(ct: cts.Token);
        Assert.True(File.Exists(Path.Combine(temp.Path, "version_manifest_v2.json")));

        // 新实例（内存为空）应命中磁盘缓存，不再发请求。
        var second = new VersionManifestService(downloader, resolver, log, temp.Path);
        var cached = await second.GetAsync(ct: cts.Token);
        Assert.Equal(manifest.Versions.Count, cached.Versions.Count);
    }

    [Fact]
    public async Task 清单里找得到指定版本且带sha1()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log)
        {
            VersionListSource = DownloadSourcePreference.Official,
            FileSource = DownloadSourcePreference.Official,
        };
        var service = new VersionManifestService(downloader, resolver, log, temp.Path);
        using var cts = new CancellationTokenSource(Timeout);

        var entry = await service.FindAsync("1.20.4", cts.Token);

        Assert.NotNull(entry);
        Assert.Equal("release", entry!.Type);
        Assert.False(string.IsNullOrWhiteSpace(entry.Sha1));
        Assert.StartsWith("https://piston-meta.mojang.com/", entry.Url);
    }

    [Fact]
    public async Task 不存在的版本返回空()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log)
        {
            VersionListSource = DownloadSourcePreference.Official,
            FileSource = DownloadSourcePreference.Official,
        };
        var service = new VersionManifestService(downloader, resolver, log, temp.Path);
        using var cts = new CancellationTokenSource(Timeout);

        var entry = await service.FindAsync("99.99.99-not-a-version", cts.Token);
        Assert.Null(entry);
    }
}
