namespace MCLauncher.Core.Models;

/// <summary>资源安装源的标识。</summary>
public enum ResourceSource
{
    /// <summary>Modrinth（默认，无需 API Key）。</summary>
    Modrinth,

    /// <summary>CurseForge（需要 API Key）。</summary>
    CurseForge,
}

/// <summary>Modrinth 搜索列表里的一个项目。</summary>
public sealed class ModrinthProject
{
    public required string Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    /// <summary>mod / resourcepack / shader。</summary>
    public string ProjectType { get; init; } = "mod";

    public required string Name { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? IconUrl { get; init; }

    public string? Author { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    public long DownloadCount { get; init; }

    public string? WebsiteUrl => string.IsNullOrWhiteSpace(Slug)
        ? null
        : $"https://modrinth.com/{(string.IsNullOrWhiteSpace(ProjectType) ? "mod" : ProjectType)}/{Slug}";

    public string DownloadText => DownloadCount switch
    {
        >= 100_000_000 => $"{DownloadCount / 100_000_000.0:0.#} 亿",
        >= 10_000 => $"{DownloadCount / 10_000.0:0.#} 万",
        _ => DownloadCount.ToString(),
    };
}

/// <summary>一页 Modrinth 搜索结果。</summary>
public sealed class ModrinthSearchPage
{
    public IReadOnlyList<ModrinthProject> Projects { get; init; } = Array.Empty<ModrinthProject>();

    public int TotalCount { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

/// <summary>Modrinth 版本对象里的一个文件。</summary>
public sealed class ModrinthVersionFile
{
    public required string FileName { get; init; }

    public string? DownloadUrl { get; init; }

    public string? Sha1 { get; init; }

    public long Size { get; init; } = -1;

    public bool IsPrimary { get; init; }
}

/// <summary>Modrinth 的一个项目版本。</summary>
public sealed class ModrinthVersion
{
    public required string Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string VersionNumber { get; init; } = string.Empty;

    /// <summary>release / beta / alpha。</summary>
    public string VersionType { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; init; }

    public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ModrinthVersionFile> Files { get; init; } = Array.Empty<ModrinthVersionFile>();

    public IReadOnlyList<ModrinthDependency> Dependencies { get; init; } = Array.Empty<ModrinthDependency>();

    public bool IsRelease => string.Equals(VersionType, "release", StringComparison.OrdinalIgnoreCase);
}

/// <summary>版本里声明的前置依赖。</summary>
public sealed class ModrinthDependency
{
    public string? ProjectId { get; init; }

    public string? VersionId { get; init; }

    /// <summary>required / optional / incompatible / embedded。</summary>
    public string DependencyType { get; init; } = string.Empty;
}
