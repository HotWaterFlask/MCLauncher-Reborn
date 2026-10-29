using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>资源下载落盘与失败清理。</summary>
public sealed class ResourceDownloadServiceTests
{
    [Fact]
    public async Task 替换下载失败后不留下临时文件且保留原文件()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        using var downloader = new HttpDownloader(log, new FakeHttpHandler("x", failFirstRequests: 1000));
        var service = new ResourceDownloadService(downloader, new DownloadSourceResolver(log), log);

        var instance = temp.File("instance");
        var mods = Path.Combine(instance, "mods");
        Directory.CreateDirectory(mods);

        var existing = Path.Combine(mods, "old.jar");
        File.WriteAllText(existing, "old");

        var result = await service.DownloadAsync(
            new ResourceDownloadRequest
            {
                Kind = ResourceKind.Mod,
                File = new ResourceFile
                {
                    FileName = "new.jar",
                    DownloadUrl = "https://cdn.example.com/new.jar",
                    FileLength = 100,
                },
                InstanceDirectory = instance,
                ReplacePath = existing,
                TargetFileName = "new.jar",
            },
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(File.Exists(existing), "替换失败时原文件必须保留");
        Assert.Empty(Directory.GetFiles(mods, "*.download*"));
    }
}
