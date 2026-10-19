using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class ArgumentEntryTests
{
    [Fact]
    public void 字符串态参数()
    {
        var block = JsonUtil.Deserialize<ArgumentsBlock>("""
        {"game":["--username","${auth_player_name}"],"jvm":["-Xmx2G"]}
        """)!;

        Assert.Equal(2, block.Game.Count);
        Assert.Equal("--username", block.Game[0].Values.Single());
        Assert.False(block.Game[0].IsRuleBased);
        Assert.True(block.Game[0].Applies());
        Assert.Equal("-Xmx2G", block.Jvm.Single().Values.Single());
    }

    [Fact]
    public void 对象态参数的单个值与数组值都能解析()
    {
        var block = JsonUtil.Deserialize<ArgumentsBlock>("""
        {
          "game": [
            {"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"--demo"},
            {"rules":[{"action":"allow","os":{"name":"linux"}}],"value":["-a","-b"]}
          ],
          "jvm": []
        }
        """)!;

        Assert.Equal(2, block.Game.Count);

        Assert.True(block.Game[0].IsRuleBased);
        Assert.Equal("--demo", block.Game[0].Values.Single());

        Assert.Equal(new[] { "-a", "-b" }, block.Game[1].Values);
        Assert.Single(block.Game[1].Rules!);
    }

    [Fact]
    public void 对象态参数按显式环境求值()
    {
        var block = JsonUtil.Deserialize<ArgumentsBlock>("""
        {"game":[{"rules":[{"action":"allow","os":{"name":"linux"}}],"value":"--only-linux"}],"jvm":[]}
        """)!;
        var entry = block.Game.Single();

        Assert.True(RuleEvaluator.AppliesToEnvironment(entry.Rules, "linux", "6.1", "x86_64"));
        Assert.False(RuleEvaluator.AppliesToEnvironment(entry.Rules, "windows", "10.0", "x86_64"));
    }

    [Fact]
    public void 写回时保持原有形态()
    {
        var block = JsonUtil.Deserialize<ArgumentsBlock>("""
        {"game":["plain",{"rules":[{"action":"allow"}],"value":["x","y"]}],"jvm":[]}
        """)!;

        var json = JsonUtil.Serialize(block);

        // 字符串态仍写字符串；对象态仍写"rules + value 数组"。
        Assert.Contains("\"plain\"", json);
        Assert.Contains("\"rules\"", json);
        Assert.Contains("\"x\"", json);
        Assert.Contains("\"y\"", json);
    }

    [Fact]
    public void 老版本只用minecraftArguments时判定为旧式参数()
    {
        var version = JsonUtil.Deserialize<GameVersionJson>("""
        {"id":"1.7.10","minecraftArguments":"${auth_player_name} ${version_type}"}
        """)!;

        Assert.True(version.UsesLegacyArguments);
        Assert.Equal(8, version.RequiredJavaMajor);
    }

    [Fact]
    public void 新版本用arguments时判定为新式参数()
    {
        var version = JsonUtil.Deserialize<GameVersionJson>("""
        {"id":"1.20.4","arguments":{"game":["--username"],"jvm":["-Xmx2G"]},"javaVersion":{"majorVersion":17}}
        """)!;

        Assert.False(version.UsesLegacyArguments);
        Assert.Equal(17, version.RequiredJavaMajor);
    }
}
