using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 合并顺序是这个模块的全部意义：libraries 子在前父在后、arguments 父在前子在后。
/// 这两条顺序一旦写反，加载器版本要么起不来，要么用到错误的类。
/// </summary>
public sealed class VersionResolverTests
{
    private const string ParentJson = """
    {
      "id": "1.20.4",
      "mainClass": "net.minecraft.client.main.Main",
      "assetIndex": {"id":"1.20","sha1":"aaa","size":100,"url":"https://piston-meta.mojang.com/v1/packages/aaa/1.20.json"},
      "assets": "1.20",
      "javaVersion": {"majorVersion": 17},
      "downloads": {"client": {"sha1":"ccc","size":999,"url":"https://piston-data.mojang.com/v1/objects/ccc/client.jar"}},
      "arguments": {
        "game": ["--username", "${auth_player_name}", "--version", "${version_name}"],
        "jvm": ["-Djava.library.path=${natives_directory}", "-cp", "${classpath}"]
      },
      "libraries": [
        {"name":"com.mojang:brigadier:1.1.8"},
        {"name":"org.ow2.asm:asm:9.6"}
      ]
    }
    """;

    private const string ChildJson = """
    {
      "id": "1.20.4-forge-49.0.30",
      "inheritsFrom": "1.20.4",
      "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
      "arguments": {
        "game": ["--launchTarget", "forgeclient"],
        "jvm": ["-p", "forge.jar"]
      },
      "libraries": [
        {"name":"net.minecraftforge:forge:1.20.4-49.0.30"},
        {"name":"org.ow2.asm:asm:9.6"}
      ]
    }
    """;

    private static (VersionResolver Resolver, GameVersionJson Child, Func<string, GameVersionJson?> Lookup) Setup()
    {
        var resolver = new VersionResolver(new TestLog());
        var parent = JsonUtil.Deserialize<GameVersionJson>(ParentJson)!;
        var child = JsonUtil.Deserialize<GameVersionJson>(ChildJson)!;
        return (resolver, child, id => id == "1.20.4" ? parent : null);
    }

    [Fact]
    public void 无继承时原样返回()
    {
        var resolver = new VersionResolver(new TestLog());
        var single = JsonUtil.Deserialize<GameVersionJson>("""{"id":"1.20.4"}""")!;

        Assert.Same(single, resolver.Resolve(single, _ => null));
    }

    [Fact]
    public void 合并后标量字段以子为准()
    {
        var (resolver, child, lookup) = Setup();
        var merged = resolver.Resolve(child, lookup);

        Assert.Equal("1.20.4-forge-49.0.30", merged.Id);
        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", merged.MainClass);
        Assert.Null(merged.InheritsFrom);
    }

    [Fact]
    public void 子没有的标量字段回退到父()
    {
        var (resolver, child, lookup) = Setup();
        var merged = resolver.Resolve(child, lookup);

        Assert.Equal("1.20", merged.Assets);
        Assert.Equal("1.20", merged.AssetIndex!.Id);
        Assert.Equal(17, merged.RequiredJavaMajor);
        Assert.Equal("ccc", merged.Downloads!.Client!.Sha1);
    }

    [Fact]
    public void libraries是子在前父在后且重复项以子为准()
    {
        var (resolver, child, lookup) = Setup();
        var merged = resolver.Resolve(child, lookup);

        var names = merged.Libraries!.Select(l => l.Name).ToList();

        Assert.Equal(
            new[]
            {
                "net.minecraftforge:forge:1.20.4-49.0.30",
                "org.ow2.asm:asm:9.6",
                "com.mojang:brigadier:1.1.8",
            },
            names);
    }

    [Fact]
    public void arguments是父在前子在后()
    {
        var (resolver, child, lookup) = Setup();
        var merged = resolver.Resolve(child, lookup);

        var game = merged.Arguments!.Game.Select(e => e.Values.Single()).ToList();
        Assert.Equal(
            new[] { "--username", "${auth_player_name}", "--version", "${version_name}", "--launchTarget", "forgeclient" },
            game);

        var jvm = merged.Arguments.Jvm.Select(e => e.Values.Single()).ToList();
        Assert.Equal(
            new[] { "-Djava.library.path=${natives_directory}", "-cp", "${classpath}", "-p", "forge.jar" },
            jvm);
    }

    [Fact]
    public void 老式minecraftArguments也按子优先父兜底合并()
    {
        var resolver = new VersionResolver(new TestLog());
        var parent = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.7.10","minecraftArguments":"${auth_player_name} ${version_type}"}""")!;
        var child = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.7.10-forge","inheritsFrom":"1.7.10","minecraftArguments":"${auth_player_name} --tweakClass x"}""")!;

        var merged = resolver.Resolve(child, id => id == "1.7.10" ? parent : null);
        Assert.Contains("--tweakClass", merged.MinecraftArguments);
    }

    [Fact]
    public void 三层继承链按顺序合并()
    {
        var resolver = new VersionResolver(new TestLog());
        var root = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"root:lib:1"}],"arguments":{"game":["root"],"jvm":[]}}""")!;
        var middle = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.20.1-forge","inheritsFrom":"1.20.1","libraries":[{"name":"forge:lib:1"}],"arguments":{"game":["middle"],"jvm":[]}}""")!;
        var leaf = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.20.1-forge-optifine","inheritsFrom":"1.20.1-forge","libraries":[{"name":"opti:lib:1"}]}""")!;

        var lookup = new Dictionary<string, GameVersionJson> { ["1.20.1"] = root, ["1.20.1-forge"] = middle };
        var merged = resolver.Resolve(leaf, id => lookup.GetValueOrDefault(id));

        Assert.Equal(
            new[] { "opti:lib:1", "forge:lib:1", "root:lib:1" },
            merged.Libraries!.Select(l => l.Name));
        Assert.Equal(new[] { "root", "middle" }, merged.Arguments!.Game.Select(e => e.Values.Single()));
    }

    [Fact]
    public void 循环继承会被检测出来()
    {
        var resolver = new VersionResolver(new TestLog());
        var a = JsonUtil.Deserialize<GameVersionJson>("""{"id":"a","inheritsFrom":"b"}""")!;
        var b = JsonUtil.Deserialize<GameVersionJson>("""{"id":"b","inheritsFrom":"a"}""")!;

        Assert.Throws<InvalidDataException>(() =>
            resolver.Resolve(a, id => id == "b" ? b : a));
    }

    [Fact]
    public void 找不到父版本时报错()
    {
        var resolver = new VersionResolver(new TestLog());
        var child = JsonUtil.Deserialize<GameVersionJson>("""{"id":"x","inheritsFrom":"missing"}""")!;

        Assert.Throws<InvalidDataException>(() => resolver.Resolve(child, _ => null));
    }
}
