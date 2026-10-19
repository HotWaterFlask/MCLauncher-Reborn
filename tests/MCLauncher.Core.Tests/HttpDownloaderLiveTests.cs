using System.Text.Json;
using MCLauncher.Core.Network;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 真实网络验证：确认下载器能取到官方清单、能在镜像不可用时自动回退、
/// 能对客户端/资源索引做真实 SHA-1 校验。这些用例会访问外网，属于里程碑的"硬验证"。
/// </summary>
public sealed class HttpDownloaderLiveTests
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    /// <summary>
    /// 注意：<c>version_manifest_v2.json</c> 的版本条目**没有 size 字段**，
    /// 只有 id/type/url/time/releaseTime/sha1/complianceLevel。size 只出现在版本 JSON 的 downloads 里。
    /// </summary>
    private static (string Id, string Url, string Sha1, long Size) ReadLatestRelease(string manifest)
    {
        using var doc = JsonDocument.Parse(manifest);
        var latestId = doc.RootElement.GetProperty("latest").GetProperty("release").GetString()!;

        foreach (var entry in doc.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() != latestId)
            {
                continue;
            }

            var size = entry.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var value)
                ? value
                : -1L;

            return (
                latestId,
                entry.GetProperty("url").GetString()!,
                entry.GetProperty("sha1").GetString()!,
                size);
        }

        throw new InvalidOperationException($"清单中找不到最新正式版：{latestId}");
    }

    private static (string Url, string Sha1, long Size) ReadAssetIndex(string versionJson)
    {
        using var doc = JsonDocument.Parse(versionJson);
        var index = doc.RootElement.GetProperty("assetIndex");
        var size = index.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var value)
            ? value
            : -1L;

        return (
            index.GetProperty("url").GetString()!,
            index.GetProperty("sha1").GetString()!,
            size);
    }

    [Fact]
    public async Task 从官方源获取版本清单()
    {
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log) { PreferMirror = false };
        using var cts = new CancellationTokenSource(Timeout);

        var json = await downloader.GetStringAsync(resolver.Resolve(ManifestUrl, DownloadUrlKind.Metadata), cts.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("latest", out _));
        Assert.True(doc.RootElement.GetProperty("versions").GetArrayLength() > 100, "清单里应有大量版本条目");
    }

    [Fact]
    public async Task 镜像不可用时自动回退到官方源()
    {
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log) { PreferMirror = true };
        using var cts = new CancellationTokenSource(Timeout);

        // 本机环境访问 BMCLAPI 会超时，正好用来验证"镜像挂了也能装"。
        var candidates = resolver.Resolve(ManifestUrl, DownloadUrlKind.Metadata);
        Assert.True(candidates.Count >= 2, "应同时包含镜像与官方候选");

        var json = await downloader.GetStringAsync(candidates, cts.Token);
        Assert.Contains("\"versions\"", json);
    }

    [Fact]
    public async Task 下载最新正式版JSON并按SHA1校验()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log) { PreferMirror = false };
        using var cts = new CancellationTokenSource(Timeout);

        var manifest = await downloader.GetStringAsync(resolver.Resolve(ManifestUrl, DownloadUrlKind.Metadata), cts.Token);
        var (id, url, sha1, size) = ReadLatestRelease(manifest);

        var target = temp.File(id + ".json");
        var checker = FileChecker.Of(sha1, size);
        var candidates = resolver.Resolve(url, DownloadUrlKind.Metadata);

        var outcome = await downloader.DownloadAsync(candidates, target, checker, null, cts.Token);
        Assert.True(outcome.Success, outcome.Error);

        using var doc = JsonDocument.Parse(File.ReadAllText(target));
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetString());

        var again = await downloader.DownloadAsync(candidates, target, checker, null, cts.Token);
        Assert.True(again.Skipped, "第二次下载应因校验通过而跳过");
    }

    [Fact]
    public async Task 批量下载版本JSON与资源索引()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log);
        var resolver = new DownloadSourceResolver(log) { PreferMirror = false };
        using var cts = new CancellationTokenSource(Timeout);

        var manifest = await downloader.GetStringAsync(resolver.Resolve(ManifestUrl, DownloadUrlKind.Metadata), cts.Token);
        var (id, url, sha1, size) = ReadLatestRelease(manifest);

        var versionJson = await downloader.GetStringAsync(resolver.Resolve(url, DownloadUrlKind.Metadata), cts.Token);
        var (indexUrl, indexSha1, indexSize) = ReadAssetIndex(versionJson);

        var batch = new DownloadBatch(downloader, log);
        var requests = new List<DownloadRequest>
        {
            new()
            {
                Name = id + ".json",
                Candidates = resolver.Resolve(url, DownloadUrlKind.Metadata),
                TargetPath = temp.File(id + ".json"),
                Checker = FileChecker.Of(sha1, size),
            },
            new()
            {
                Name = "asset-index.json",
                Candidates = resolver.Resolve(indexUrl, DownloadUrlKind.AssetIndex),
                TargetPath = temp.File("asset-index.json"),
                Checker = FileChecker.Of(indexSha1, indexSize),
            },
        };

        var result = await batch.RunAsync(requests, maxParallel: 2, progress: null, ct: cts.Token);

        Assert.True(result.Success, string.Join("；", result.Failures.Select(f => f.Name + ":" + f.Error)));
        Assert.Equal(2, result.Completed);
        Assert.True(File.Exists(temp.File("asset-index.json")));

        // 资源索引必须真的是索引：有 objects 字典且条目数以千计。
        using var indexDoc = JsonDocument.Parse(File.ReadAllText(temp.File("asset-index.json")));
        Assert.True(indexDoc.RootElement.GetProperty("objects").EnumerateObject().Any());
    }
}
