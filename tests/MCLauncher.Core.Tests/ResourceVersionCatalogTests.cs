using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

namespace MCLauncher.Core.Tests;

/// <summary>详情页的版本推荐与分组规则。</summary>
public class ResourceVersionCatalogTests
{
    private static ResourceVersionInfo MakeVersion(
        string name,
        string versionType,
        DateTimeOffset published,
        string[] gameVersions,
        string[] loaders,
        ResourceSource source = ResourceSource.Modrinth)
    {
        return new ResourceVersionInfo
        {
            Source = source,
            Id = name,
            Name = name,
            VersionType = versionType,
            PublishedAt = published,
            GameVersions = gameVersions,
            Loaders = loaders,
            File = new ResourceFile
            {
                FileName = name + ".jar",
                DownloadUrl = "https://cdn.modrinth.com/" + name + ".jar",
            },
        };
    }

    [Fact]
    public void 推荐版本按实例版本与加载器挑选最新正式版()
    {
        var versions = new[]
        {
            MakeVersion("newest-beta", "beta", DateTimeOffset.Parse("2026-10-20T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
            MakeVersion("matched-release", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
            MakeVersion("other-mc", "release", DateTimeOffset.Parse("2026-10-15T00:00:00Z"),
                new[] { "26.2" }, new[] { "fabric" }),
        };

        var recommended = ResourceVersionCatalog.PickRecommended(
            versions, ResourceKind.Mod, "26.3", "Fabric");

        Assert.NotNull(recommended);
        Assert.Equal("matched-release", recommended!.Name);
    }

    [Fact]
    public void 没有正式版时推荐最新的测试版()
    {
        var versions = new[]
        {
            MakeVersion("beta-old", "beta", DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
            MakeVersion("beta-new", "beta", DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
        };

        var recommended = ResourceVersionCatalog.PickRecommended(
            versions, ResourceKind.Mod, "26.3", "Fabric");

        Assert.Equal("beta-new", recommended!.Name);
    }

    [Fact]
    public void 推荐时优先同加载器再回退到通用文件()
    {
        var versions = new[]
        {
            MakeVersion("generic", "release", DateTimeOffset.Parse("2026-10-12T00:00:00Z"),
                new[] { "26.3" }, Array.Empty<string>()),
            MakeVersion("forge", "release", DateTimeOffset.Parse("2026-10-11T00:00:00Z"),
                new[] { "26.3" }, new[] { "forge" }),
            MakeVersion("fabric", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
        };

        var recommended = ResourceVersionCatalog.PickRecommended(
            versions, ResourceKind.Mod, "26.3", "Fabric");

        Assert.Equal("fabric", recommended!.Name);
    }

    [Fact]
    public void 不同加载器的版本不算兼容()
    {
        var version = MakeVersion("forge", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
            new[] { "26.3", "Forge" }, new[] { "forge" });

        Assert.False(ResourceVersionCatalog.IsCompatible(
            version, ResourceKind.Mod, "26.3", "Fabric"));
    }

    [Fact]
    public void 没有加载器标签的版本视为通用()
    {
        var version = MakeVersion("universal", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
            new[] { "26.3" }, Array.Empty<string>());

        Assert.True(ResourceVersionCatalog.IsCompatible(
            version, ResourceKind.Mod, "26.3", "Fabric"));
    }

    [Fact]
    public void 按游戏版本分组时会剔除加载器名()
    {
        var versions = new[]
        {
            MakeVersion("fabric-263", "release", DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
                new[] { "26.3", "Fabric" }, new[] { "fabric" }),
            MakeVersion("fabric-262", "release", DateTimeOffset.Parse("2026-09-10T00:00:00Z"),
                new[] { "26.2", "Fabric" }, new[] { "fabric" }),
        };

        var groups = ResourceVersionCatalog.GroupByGameVersion(versions);

        Assert.Equal(2, groups.Count);
        Assert.Equal("26.3", groups[0].GameVersion);
        Assert.Equal("26.2", groups[1].GameVersion);
        Assert.DoesNotContain(groups, group => group.GameVersion.Equals("Fabric", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 分组按最近发布时间排序且组内倒序()
    {
        var versions = new[]
        {
            MakeVersion("old-263", "release", DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
            MakeVersion("new-263", "release", DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
                new[] { "26.3" }, new[] { "fabric" }),
            MakeVersion("only-262", "release", DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
                new[] { "26.2" }, new[] { "fabric" }),
        };

        var groups = ResourceVersionCatalog.GroupByGameVersion(versions);

        Assert.Equal("26.3", groups[0].GameVersion);
        Assert.Equal("new-263", groups[0].Versions[0].Name);
        Assert.Equal("old-263", groups[0].Versions[1].Name);
    }

    [Theory]
    [InlineData("26.3", true)]
    [InlineData("1.21.4", true)]
    [InlineData("Fabric", false)]
    [InlineData("Java 21", false)]
    [InlineData("", false)]
    public void 判断字符串是否像游戏版本号(string value, bool expected)
    {
        Assert.Equal(expected, ResourceVersionCatalog.IsGameVersionName(value));
    }
}
