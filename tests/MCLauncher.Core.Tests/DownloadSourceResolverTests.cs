using MCLauncher.Core.Network;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 镜像映射是"按 URL 类别分别处理"的，这里逐类锁定行为，防止将来被改成"一刀切换域名"。
/// </summary>
public sealed class DownloadSourceResolverTests
{
    private static DownloadSourceResolver Create(bool preferMirror = true) =>
        new(new TestLog()) { PreferMirror = preferMirror };

    [Fact]
    public void 元数据走镜像根路径()
    {
        var resolver = Create();
        var result = resolver.Resolve(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            DownloadUrlKind.Metadata);

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
            result[0].Url);
        Assert.Contains(result, c => c.Url.StartsWith("https://piston-meta.mojang.com/", StringComparison.Ordinal));
    }

    [Fact]
    public void 资源对象落到镜像的assets子路径()
    {
        var resolver = Create();
        var result = resolver.Resolve(
            "https://resources.download.minecraft.net/ab/abcdef0123456789",
            DownloadUrlKind.Asset);

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/assets/ab/abcdef0123456789",
            result[0].Url);
    }

    [Fact]
    public void 库文件同时给出maven与libraries两个镜像候选()
    {
        var resolver = Create();
        var result = resolver.Resolve(
            "https://libraries.minecraft.net/org/ow2/asm/asm/9.6/asm-9.6.jar",
            DownloadUrlKind.Library);

        var mirrorUrls = result.Where(c => c.SourceName.StartsWith("BMCLAPI", StringComparison.Ordinal))
            .Select(c => c.Url)
            .ToList();

        Assert.Contains("https://bmclapi2.bangbang93.com/maven/org/ow2/asm/asm/9.6/asm-9.6.jar", mirrorUrls);
        Assert.Contains("https://bmclapi2.bangbang93.com/libraries/org/ow2/asm/asm/9.6/asm-9.6.jar", mirrorUrls);
    }

    [Fact]
    public void 客户端jar在已知版本号时给出镜像的versions短路径()
    {
        var resolver = Create();
        var result = resolver.Resolve(
            "https://piston-data.mojang.com/v1/objects/deadbeef/client.jar",
            DownloadUrlKind.Client,
            "1.20.4");

        Assert.Contains(
            result,
            c => c.Url == "https://bmclapi2.bangbang93.com/versions/1.20.4/1.20.4.jar");
    }

    [Fact]
    public void 旧版明文http地址会被升级为https()
    {
        var resolver = Create(preferMirror: false);
        var result = resolver.Resolve(
            "http://resources.download.minecraft.net/ab/abcdef",
            DownloadUrlKind.Asset);

        Assert.Equal("https://resources.download.minecraft.net/ab/abcdef", result[0].Url);
    }

    [Fact]
    public void 优先官方时官方排在首位且镜像仍然保留()
    {
        var resolver = Create(preferMirror: false);
        var result = resolver.Resolve(
            "https://libraries.minecraft.net/gson.jar",
            DownloadUrlKind.Library);

        Assert.Equal("官方", result[0].SourceName);
        Assert.Contains(result, c => c.SourceName.StartsWith("BMCLAPI", StringComparison.Ordinal));
    }

    [Fact]
    public void 自定义地址不做镜像映射()
    {
        var resolver = Create();
        var result = resolver.Resolve("https://example.com/foo/bar.jar", DownloadUrlKind.Custom);

        Assert.Single(result);
        Assert.Equal("https://example.com/foo/bar.jar", result[0].Url);
    }

    [Fact]
    public void Mod平台官方源始终排第一()
    {
        var resolver = Create(preferMirror: true);
        var result = resolver.Resolve(
            "https://api.modrinth.com/v2/project/fabric-api",
            DownloadUrlKind.ModPlatform);

        Assert.Equal("官方", result[0].SourceName);
        Assert.Contains(result, c => c.SourceName == "MCIMirror");
    }

    [Fact]
    public void 候选源不重复()
    {
        var resolver = Create();
        var result = resolver.Resolve(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            DownloadUrlKind.Metadata);

        var distinct = result.Select(c => c.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(result.Count, distinct);
    }
}
