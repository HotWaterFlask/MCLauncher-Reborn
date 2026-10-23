using MCLauncher.Core.Models;

namespace MCLauncher.Core.Services;

/// <summary>
/// 版本推荐与分组规则。
/// <para>
/// 推荐版本 = 与当前实例的游戏版本、加载器都匹配的版本里最新的正式版；
/// 分组则把同一个版本挂到它支持的每个 Minecraft 版本下（CurseForge 的
/// <c>gameVersions</c> 会把加载器名混在一起，必须先剔除）。
/// </para>
/// </summary>
public static class ResourceVersionCatalog
{
    private static readonly HashSet<string> LoaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge", "neoforge", "fabric", "quilt", "liteloader", "rift", "cauldron",
    };

    /// <summary>该字符串是否是加载器名（而不是游戏版本号）。</summary>
    public static bool IsLoaderName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && LoaderNames.Contains(value.Trim());

    /// <summary>该字符串是否像一个游戏版本号。</summary>
    public static bool IsGameVersionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        return !IsLoaderName(text)
               && !text.StartsWith("Java ", StringComparison.OrdinalIgnoreCase)
               && text.Any(char.IsDigit);
    }

    /// <summary>版本是否兼容给定的游戏版本与加载器。</summary>
    public static bool IsCompatible(
        ResourceVersionInfo version,
        ResourceKind kind,
        string? gameVersion,
        string? loaderName)
    {
        if (!string.IsNullOrWhiteSpace(gameVersion)
            && !version.GameVersions.Any(value =>
                string.Equals(value.Trim(), gameVersion.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (kind != ResourceKind.Mod)
        {
            return true;
        }

        var loader = ModrinthService.NormalizeLoader(loaderName);
        if (loader is null)
        {
            return true;
        }

        var tags = version.Loaders.Count > 0 ? version.Loaders : version.GameVersions;
        var known = tags.Where(IsLoaderName).ToList();

        // 没有标注加载器的版本通常是不依赖加载器的通用文件，放行。
        return known.Count == 0
               || known.Any(value =>
                   string.Equals(value.Trim(), loader, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>挑选推荐版本：加载器匹配优先，其次正式版，再按发布时间取最新。</summary>
    public static ResourceVersionInfo? PickRecommended(
        IReadOnlyList<ResourceVersionInfo> versions,
        ResourceKind kind,
        string? gameVersion,
        string? loaderName)
    {
        var candidates = versions
            .Where(version => IsCompatible(version, kind, gameVersion, loaderName))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var loader = kind == ResourceKind.Mod ? ModrinthService.NormalizeLoader(loaderName) : null;
        if (loader is not null)
        {
            var withLoader = candidates
                .Where(version => version.Loaders.Any(value =>
                    string.Equals(value.Trim(), loader, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (withLoader.Count > 0)
            {
                candidates = withLoader;
            }
        }

        var ordered = candidates.OrderByDescending(version => version.PublishedAt).ToList();
        return ordered.FirstOrDefault(version => version.IsRelease)
               ?? ordered.FirstOrDefault(version =>
                   string.Equals(version.VersionType, "beta", StringComparison.OrdinalIgnoreCase))
               ?? ordered[0];
    }

    /// <summary>按游戏版本分组；组内与组间都按发布时间倒序。</summary>
    public static IReadOnlyList<ResourceVersionGroup> GroupByGameVersion(
        IReadOnlyList<ResourceVersionInfo> versions)
    {
        var map = new Dictionary<string, List<ResourceVersionInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var version in versions)
        {
            foreach (var gameVersion in version.GameVersions
                         .Where(IsGameVersionName)
                         .Select(value => value.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!map.TryGetValue(gameVersion, out var list))
                {
                    list = new List<ResourceVersionInfo>();
                    map[gameVersion] = list;
                }

                list.Add(version);
            }
        }

        return map
            .Select(pair => new ResourceVersionGroup
            {
                GameVersion = pair.Key,
                Versions = pair.Value
                    .OrderByDescending(version => version.PublishedAt)
                    .ToList(),
            })
            .OrderByDescending(group => group.Versions[0].PublishedAt)
            .ToList();
    }
}
