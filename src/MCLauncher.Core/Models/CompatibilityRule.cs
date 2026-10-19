using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Models;

/// <summary>规则动作。</summary>
public enum RuleAction
{
    Allow,
    Disallow,
}

/// <summary><c>rules[].os</c> 子对象。</summary>
public sealed class OsRestriction
{
    /// <summary>windows / osx / linux。只有这三个值有约定含义，其它一律视为不匹配。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>对 <c>os.version</c> 的正则，例如 <c>^10\.</c>。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>x86 / x86_64 / arm64。</summary>
    [JsonPropertyName("arch")]
    public string? Arch { get; set; }
}

/// <summary>
/// 版本 JSON 里的兼容性规则。语义（与原版启动器一致）：
/// <list type="bullet">
/// <item>rules 为空 → 允许（老版本 JSON 里普遍没有 rules）。</item>
/// <item>rules 非空 → **默认禁止**，按顺序求值，命中的规则用它的 action 覆盖默认值。</item>
/// </list>
/// </summary>
public sealed class CompatibilityRule
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("os")]
    public OsRestriction? Os { get; set; }

    /// <summary>特性开关：<c>is_demo_user</c>、<c>has_custom_resolution</c>、<c>quick_play_*</c> 等。</summary>
    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }

    [JsonIgnore]
    public bool IsDisallow => string.Equals(Action, "disallow", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public RuleAction RuleAction => IsDisallow ? RuleAction.Disallow : RuleAction.Allow;

    /// <summary>该条规则在当前环境（或指定环境）下是否命中。</summary>
    public bool Matches(
        string osName,
        string? osVersion,
        string architecture,
        IReadOnlyDictionary<string, bool>? features)
    {
        if (Os is not null)
        {
            if (!string.IsNullOrEmpty(Os.Name)
                && !Os.Name.Equals(osName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(Os.Arch)
                && !Os.Arch.Equals(architecture, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(Os.Version))
            {
                // os.version 是正则；版本串缺失时视为不匹配。
                if (string.IsNullOrEmpty(osVersion))
                {
                    return false;
                }

                try
                {
                    if (!Regex.IsMatch(osVersion, Os.Version))
                    {
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    // 版本 JSON 里偶尔出现写坏的正则，视为不匹配即可。
                    return false;
                }
            }
        }

        if (Features is { Count: > 0 })
        {
            foreach (var (key, expected) in Features)
            {
                var actual = features is not null && features.TryGetValue(key, out var value) && value;
                if (actual != expected)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>规则集合求值。</summary>
public static class RuleEvaluator
{
    /// <summary>按当前进程环境求值。</summary>
    public static bool AppliesToCurrentEnvironment(
        IReadOnlyList<CompatibilityRule>? rules,
        IReadOnlyDictionary<string, bool>? features = null) =>
        AppliesToEnvironment(rules, GameEnvironment.OsName, GameEnvironment.OsVersion, GameEnvironment.Architecture, features);

    /// <summary>按指定环境求值（便于测试与"为别的平台预装"这类场景）。</summary>
    public static bool AppliesToEnvironment(
        IReadOnlyList<CompatibilityRule>? rules,
        string osName,
        string? osVersion,
        string architecture,
        IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        // 有 rules 时默认禁止，由命中的规则决定最终结果。
        var action = RuleAction.Disallow;
        foreach (var rule in rules)
        {
            if (rule.Matches(osName, osVersion, architecture, features))
            {
                action = rule.RuleAction;
            }
        }

        return action == RuleAction.Allow;
    }
}
