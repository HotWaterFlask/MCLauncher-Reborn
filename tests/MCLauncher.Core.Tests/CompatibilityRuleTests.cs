using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 规则语义必须与原版启动器一致：rules 为空 → 允许；rules 非空 → 默认禁止，
/// 命中的规则覆盖默认值。这里用显式环境求值，保证在任何操作系统上结论都一样。
/// </summary>
public sealed class CompatibilityRuleTests
{
    private const string Windows = "windows";
    private const string Linux = "linux";

    private static List<CompatibilityRule> Rules(string json) =>
        JsonUtil.Deserialize<List<CompatibilityRule>>(json)!;

    [Fact]
    public void 无规则时允许()
    {
        Assert.True(RuleEvaluator.AppliesToEnvironment(null, Windows, "10.0", "x86_64"));
        Assert.True(RuleEvaluator.AppliesToEnvironment(Array.Empty<CompatibilityRule>(), Windows, "10.0", "x86_64"));
    }

    [Fact]
    public void 只有允许规则时未命中就禁止()
    {
        var rules = Rules("""[{"action":"allow","os":{"name":"windows"}}]""");

        Assert.True(RuleEvaluator.AppliesToEnvironment(rules, Windows, "10.0", "x86_64"));
        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, Linux, "6.1", "x86_64"));
    }

    [Fact]
    public void 禁止规则命中时禁止()
    {
        var rules = Rules("""[{"action":"disallow","os":{"name":"osx"}}]""");

        // 非 osx 不命中 → 保持默认禁止
        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, Windows, "10.0", "x86_64"));
        // 命中 disallow → 仍为禁止
        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, "osx", "14.0", "arm64"));
    }

    [Fact]
    public void 允许与禁止混合时按顺序覆盖()
    {
        var rules = Rules("""[{"action":"allow"},{"action":"disallow","os":{"name":"osx"}}]""");

        Assert.True(RuleEvaluator.AppliesToEnvironment(rules, Windows, "10.0", "x86_64"));
        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, "osx", "14.0", "arm64"));
    }

    [Fact]
    public void 架构限制()
    {
        var rules = Rules("""[{"action":"allow","os":{"arch":"x86"}}]""");

        Assert.True(RuleEvaluator.AppliesToEnvironment(rules, Windows, "10.0", "x86"));
        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, Windows, "10.0", "x86_64"));
    }

    [Fact]
    public void 操作系统版本用正则匹配()
    {
        var rules = Rules("""[{"action":"allow","os":{"name":"osx","version":"^1[0-9]\\."}}]""");

        Assert.True(RuleEvaluator.AppliesToEnvironment(rules, "osx", "10.15.7", "x86_64"));
        Assert.True(RuleEvaluator.AppliesToEnvironment(rules, "osx", "15.1", "x86_64"));
        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, "osx", "9.6", "x86_64"));
    }

    [Fact]
    public void 特性开关参与判定()
    {
        var rules = Rules("""[{"action":"allow","features":{"is_demo_user":true}}]""");

        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, Windows, "10.0", "x86_64"));
        Assert.True(RuleEvaluator.AppliesToEnvironment(
            rules, Windows, "10.0", "x86_64", new Dictionary<string, bool> { ["is_demo_user"] = true }));
        Assert.False(RuleEvaluator.AppliesToEnvironment(
            rules, Windows, "10.0", "x86_64", new Dictionary<string, bool> { ["is_demo_user"] = false }));
    }

    [Fact]
    public void 写坏的正则视为不匹配而不是崩溃()
    {
        var rules = Rules("""[{"action":"allow","os":{"name":"osx","version":"^1[0-9"}}]""");

        Assert.False(RuleEvaluator.AppliesToEnvironment(rules, "osx", "10.15.7", "x86_64"));
    }

    [Fact]
    public void 当前环境求值不会抛异常()
    {
        var rules = Rules("""[{"action":"allow","os":{"name":"windows"}}]""");
        var result = RuleEvaluator.AppliesToCurrentEnvironment(rules);
        Assert.Equal(GameEnvironment.IsWindows, result);
    }
}
