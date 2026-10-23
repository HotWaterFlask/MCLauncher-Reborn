using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

namespace MCLauncher.Core.Tests;

/// <summary>Modrinth 版本挑选与资源类别映射。</summary>
public class ModrinthPickTests
{
    private static ModrinthVersion MakeVersion(
        string id,
        string type,
        DateTimeOffset published,
        string[] loaders,
        params string[] fileNames)
    {
        return new ModrinthVersion
        {
            Id = id,
            Name = id,
            VersionNumber = id,
            VersionType = type,
            PublishedAt = published,
            Loaders = loaders,
            Files = fileNames.Select(name => new ModrinthVersionFile
            {
                FileName = name,
                DownloadUrl = "https://cdn.modrinth.com/" + name,
                Sha1 = "sha1-" + name,
                Size = 100,
                IsPrimary = true,
            }).ToList(),
        };
    }

    [Fact]
    public void 正式版优先于更新的测试版()
    {
        var versions = new[]
        {
            MakeVersion("beta", "beta", DateTimeOffset.Parse("2026-10-15T00:00:00Z"), new[] { "fabric" }, "beta.jar"),
            MakeVersion("release", "release", DateTimeOffset.Parse("2026-09-15T00:00:00Z"), new[] { "fabric" }, "release.jar"),
        };

        var file = ModrinthService.PickBestFile(versions, ResourceKind.Mod, "Fabric");

        Assert.NotNull(file);
        Assert.Equal("release.jar", file!.FileName);
    }

    [Fact]
    public void 优先选择与实例加载器一致的版本()
    {
        var versions = new[]
        {
            MakeVersion("forge", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"), new[] { "forge" }, "mod-forge.jar"),
            MakeVersion("fabric", "release", DateTimeOffset.Parse("2026-09-10T00:00:00Z"), new[] { "fabric" }, "mod-fabric.jar"),
        };

        var file = ModrinthService.PickBestFile(versions, ResourceKind.Mod, "Fabric");

        Assert.NotNull(file);
        Assert.Equal("mod-fabric.jar", file!.FileName);
    }

    [Fact]
    public void 没有同加载器版本时不跨加载器挑选()
    {
        var versions = new[]
        {
            MakeVersion("forge", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"), new[] { "forge" }, "mod-forge.jar"),
        };

        var file = ModrinthService.PickBestFile(versions, ResourceKind.Mod, "Fabric");

        Assert.Null(file);
    }

    [Fact]
    public void 资源包不按加载器过滤()
    {
        var versions = new[]
        {
            MakeVersion("pack", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"), new[] { "minecraft" }, "pack.zip"),
        };

        var file = ModrinthService.PickBestFile(versions, ResourceKind.ResourcePack, "Fabric");

        Assert.NotNull(file);
        Assert.Equal("pack.zip", file!.FileName);
    }

    [Fact]
    public void 只挑符合资源类别扩展名的文件()
    {
        var version = MakeVersion(
            "shader", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"), Array.Empty<string>(),
            "notes.txt", "shader.zip");

        var file = ModrinthService.PickBestFile(new[] { version }, ResourceKind.ShaderPack, null);

        Assert.NotNull(file);
        Assert.Equal("shader.zip", file!.FileName);
    }

    [Theory]
    [InlineData(ResourceKind.Mod, "mod")]
    [InlineData(ResourceKind.ResourcePack, "resourcepack")]
    [InlineData(ResourceKind.ShaderPack, "shader")]
    [InlineData(ResourceKind.Modpack, "modpack")]
    public void 资源类别映射到项目类型(ResourceKind kind, string expected)
    {
        Assert.Equal(expected, ModrinthService.ProjectTypeFor(kind));
    }

    [Fact]
    public void 整合包支持Modrinth且只挑压缩包()
    {
        Assert.True(ModrinthService.SupportsDownload(ResourceKind.Modpack));

        var version = MakeVersion(
            "pack", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
            new[] { "minecraft" }, "notes.txt", "pack.mrpack");

        var file = ModrinthService.PickBestFile(new[] { version }, ResourceKind.Modpack, "Fabric");

        Assert.NotNull(file);
        Assert.Equal("pack.mrpack", file!.FileName);
    }

    [Fact]
    public void 世界存档不支持Modrinth()
    {
        Assert.False(ModrinthService.SupportsDownload(ResourceKind.World));
    }

    [Theory]
    [InlineData("Fabric", "fabric")]
    [InlineData("NeoForge", "neoforge")]
    [InlineData("原版", null)]
    [InlineData(null, null)]
    public void 加载器名归一化(string? input, string? expected)
    {
        Assert.Equal(expected, ModrinthService.NormalizeLoader(input));
    }
}
