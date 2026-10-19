using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class LibraryTests
{
    private static Library Parse(string json) => JsonUtil.Deserialize<Library>(json)!;

    [Fact]
    public void Maven坐标转相对路径()
    {
        var library = Parse("""{"name":"org.ow2.asm:asm:9.6"}""");
        Assert.Equal("org/ow2/asm/asm/9.6/asm-9.6.jar", library.ArtifactPath());
    }

    [Fact]
    public void 带分类器的坐标转相对路径()
    {
        var library = Parse("""{"name":"org.lwjgl:lwjgl:3.3.2:natives-windows"}""");
        Assert.Equal("org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2-natives-windows.jar", library.ArtifactPath());
    }

    [Fact]
    public void 缺失downloads时按url仓库根拼接地址()
    {
        var library = Parse("""{"name":"com.example:demo:1.0","url":"https://maven.example.com/"}""");
        Assert.Equal("https://maven.example.com/com/example/demo/1.0/demo-1.0.jar", library.ResolveDownloadUrl());
    }

    [Fact]
    public void 有downloads时优先用其中的url()
    {
        var library = Parse("""
        {"name":"com.example:demo:1.0",
         "downloads":{"artifact":{"url":"https://cdn.example.com/demo-1.0.jar","sha1":"abc","size":10}}}
        """);
        Assert.Equal("https://cdn.example.com/demo-1.0.jar", library.ResolveDownloadUrl());
        Assert.Equal("abc", library.GetDownloadInfo()!.Sha1);
    }

    [Fact]
    public void 旧版natives映射表按平台解析分类器()
    {
        var os = GameEnvironment.OsName;
        var library = Parse("""
        {"name":"org.lwjgl.lwjgl:lwjgl-platform:2.9.4",
         "natives":{"__OS__":"natives-__OS__-${arch}"},
         "downloads":{"classifiers":{"natives-__OS__-32":{"sha1":"a","size":1,"url":"https://x/32.jar"},
                                     "natives-__OS__-64":{"sha1":"b","size":1,"url":"https://x/64.jar"}}}}
        """.Replace("__OS__", os));

        var classifier = library.ResolveNativeClassifier();
        Assert.NotNull(classifier);
        Assert.EndsWith("-" + GameEnvironment.ArchitectureBits, classifier);
        Assert.True(library.IsNativeLibrary);
    }

    [Fact]
    public void 新版natives用classifiers键解析分类器()
    {
        var os = GameEnvironment.OsName;
        var library = Parse("""
        {"name":"org.lwjgl:lwjgl:3.3.2",
         "downloads":{"classifiers":{"natives-__OS__":{"sha1":"a","size":1,"url":"https://x/a.jar"},
                                     "natives-__OS__-x86":{"sha1":"b","size":1,"url":"https://x/b.jar"},
                                     "natives-linux":{"sha1":"c","size":1,"url":"https://x/c.jar"}}}}
        """.Replace("__OS__", os));

        // 必须挑出当前平台的分类器，而不是词典里第一个。
        Assert.Equal($"natives-{os}", library.ResolveNativeClassifier());
        Assert.True(library.IsNativeLibrary);
    }

    [Fact]
    public void 多架构并存时优先选与当前架构匹配的分类器()
    {
        var os = GameEnvironment.OsName;
        var library = Parse("""
        {"name":"org.lwjgl:lwjgl:3.3.2",
         "downloads":{"classifiers":{"natives-__OS__":{"sha1":"a","size":1,"url":"https://x/x64.jar"},
                                     "natives-__OS__-arm64":{"sha1":"b","size":1,"url":"https://x/arm64.jar"}}}}
        """.Replace("__OS__", os));

        var expected = GameEnvironment.Architecture == "arm64"
            ? $"natives-{os}-arm64"
            : $"natives-{os}";

        Assert.Equal(expected, library.ResolveNativeClassifier());
    }

    [Fact]
    public void 普通库不是natives()
    {
        var library = Parse("""{"name":"com.google.code.gson:gson:2.10.1"}""");
        Assert.False(library.IsNativeLibrary);
        Assert.Null(library.ResolveNativeClassifier());
    }

    [Fact]
    public void natives默认排除META_INF()
    {
        var library = Parse("""{"name":"a:b:1","natives":{"windows":"natives-windows"}}""");
        Assert.Contains("META-INF/", library.ExtractExclusions);
    }

    [Fact]
    public void 显式声明的排除项优先()
    {
        var library = Parse("""{"name":"a:b:1","extract":{"exclude":["META-INF/","custom/"]}}""");
        Assert.Equal(new[] { "META-INF/", "custom/" }, library.ExtractExclusions);
    }

    [Fact]
    public void 规则不通过时不生效()
    {
        var library = Parse("""{"name":"a:b:1","rules":[{"action":"allow","os":{"name":"osx"}}]}""");
        Assert.False(RuleEvaluator.AppliesToEnvironment(library.Rules, "windows", "10.0", "x86_64"));
        Assert.True(RuleEvaluator.AppliesToEnvironment(library.Rules, "osx", "14.0", "arm64"));
    }
}
