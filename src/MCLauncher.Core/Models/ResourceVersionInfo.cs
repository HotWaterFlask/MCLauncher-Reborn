namespace MCLauncher.Core.Models;

/// <summary>
/// 项目在资源站上的摘要信息，供详情页头部展示与前置模组跳转使用。
/// </summary>
public sealed class ResourceProjectSummary
{
    public required ResourceSource Source { get; init; }

    /// <summary>Modrinth 项目 id；CurseForge 项目为 null。</summary>
    public string? ModrinthId { get; init; }

    /// <summary>CurseForge 项目 id；Modrinth 项目为 0。</summary>
    public int CurseForgeId { get; init; }

    public required string Name { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? IconUrl { get; set; }

    public string? Author { get; set; }

    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    public long DownloadCount { get; set; }

    public string? WebsiteUrl { get; set; }

    public string DownloadText => DownloadCount switch
    {
        >= 100_000_000 => $"{DownloadCount / 100_000_000.0:0.#} 亿",
        >= 10_000 => $"{DownloadCount / 10_000.0:0.#} 万",
        _ => DownloadCount.ToString(),
    };
}

/// <summary>前置内容的性质。</summary>
public enum ResourceDependencyKind
{
    /// <summary>必需：缺失时游戏可能无法启动。</summary>
    Required,

    /// <summary>可选：缺失时游戏能运行，但部分功能缺失。</summary>
    Optional,
}

/// <summary>一个版本声明的前置项目。项目名称等元数据在打开下载弹窗时才批量补全。</summary>
public sealed class ResourceDependency
{
    public required ResourceSource Source { get; init; }

    public required ResourceDependencyKind Kind { get; init; }

    public string? ModrinthProjectId { get; init; }

    public int CurseForgeProjectId { get; init; }

    /// <summary>补全后的项目信息；为 null 表示尚未解析或解析失败。</summary>
    public ResourceProjectSummary? Project { get; set; }
}

/// <summary>资源站上一个具体的可下载版本。</summary>
public sealed class ResourceVersionInfo
{
    public required ResourceSource Source { get; init; }

    /// <summary>版本 id（Modrinth 为字符串、CurseForge 为数字转字符串）。</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>release / beta / alpha。</summary>
    public string VersionType { get; init; } = "release";

    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>支持的游戏版本（CurseForge 里还会混入加载器名）。</summary>
    public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();

    /// <summary>加载器标签（Fabric / Forge / NeoForge / Quilt）。</summary>
    public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();

    public required ResourceFile File { get; init; }

    public IReadOnlyList<ResourceDependency> Dependencies { get; init; } = Array.Empty<ResourceDependency>();

    public bool IsRelease => string.Equals(VersionType, "release", StringComparison.OrdinalIgnoreCase);
}

/// <summary>按游戏版本聚合的一组版本。</summary>
public sealed class ResourceVersionGroup
{
    public required string GameVersion { get; init; }

    public required IReadOnlyList<ResourceVersionInfo> Versions { get; init; }
}
