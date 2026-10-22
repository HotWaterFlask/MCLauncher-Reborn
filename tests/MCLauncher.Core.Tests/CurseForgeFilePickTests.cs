using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Utils;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

public sealed class CurseForgeFilePickTests
{
    private static CurseForgeFile MakeFile(
        int id,
        string name,
        int releaseType,
        string fileDate,
        params string[] gameVersions) => new()
    {
        Id = id,
        FileName = name,
        FileDate = DateTimeOffset.Parse(fileDate),
        ReleaseType = releaseType,
        GameVersions = gameVersions,
        DownloadUrl = "https://example.com/" + name,
    };

    [Fact]
    public void 优先选择加载器匹配的文件()
    {
        var files = new[]
        {
            MakeFile(1, "mod-forge-1.0.jar", 1, "2026-01-01T00:00:00Z", "1.20.1", "Forge"),
            MakeFile(2, "mod-fabric-1.0.jar", 2, "2026-02-01T00:00:00Z", "1.20.1", "Fabric"),
        };

        var picked = CurseForgeService.PickBestFile(files, ResourceKind.Mod, "Fabric");

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Id);
    }

    [Fact]
    public void 优先选择正式版()
    {
        var files = new[]
        {
            MakeFile(1, "mod-fabric-1.0.jar", 1, "2026-01-01T00:00:00Z", "1.20.1", "Fabric"),
            MakeFile(2, "mod-fabric-1.1-beta.jar", 2, "2026-03-01T00:00:00Z", "1.20.1", "Fabric"),
        };

        var picked = CurseForgeService.PickBestFile(files, ResourceKind.Mod, "Fabric");

        Assert.Equal(1, picked!.Id);
    }

    [Fact]
    public void 没有同加载器文件时返回空()
    {
        var files = new[]
        {
            MakeFile(1, "mod-forge-1.0.jar", 1, "2026-01-01T00:00:00Z", "1.20.1", "Forge"),
        };

        Assert.Null(CurseForgeService.PickBestFile(files, ResourceKind.Mod, "Fabric"));
    }

    [Fact]
    public void 资源包忽略加载器并取最新正式版()
    {
        var files = new[]
        {
            MakeFile(1, "pack-a.zip", 1, "2026-01-01T00:00:00Z", "1.20.1"),
            MakeFile(2, "pack-b.zip", 1, "2026-02-01T00:00:00Z", "1.20.1"),
        };

        var picked = CurseForgeService.PickBestFile(files, ResourceKind.ResourcePack, "Fabric");

        Assert.Equal(2, picked!.Id);
    }

    [Fact]
    public void 文件指纹等于剔除空白后的MurmurHash()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "sample.bin");
        var raw = new byte[] { 1, 0x20, 2, 0x09, 3, 0x0A, 4, 0x0D, 5, 6, 7, 0x20 };
        File.WriteAllBytes(path, raw);

        var filtered = raw.Where(value => value is not (0x09 or 0x0A or 0x0D or 0x20)).ToArray();

        Assert.Equal(MurmurHash2.Hash32(filtered), MurmurHash2.ComputeFileFingerprint(path));
    }
}
