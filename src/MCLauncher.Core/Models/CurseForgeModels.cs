namespace MCLauncher.Core.Models;

/// <summary>CurseForge 搜索排序字段（对应 API 的 sortField）。</summary>
public enum CurseForgeSort
{
    /// <summary>热度（默认）。</summary>
    Popularity = 2,

    /// <summary>最近更新。</summary>
    LastUpdated = 3,

    /// <summary>名称。</summary>
    Name = 4,

    /// <summary>总下载量。</summary>
    TotalDownloads = 6,
}

/// <summary>CurseForge 搜索列表里的一个项目。</summary>
public sealed class CurseForgeProject
{
    public required int Id { get; init; }

    public int ClassId { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public long DownloadCount { get; init; }

    public string? LogoUrl { get; init; }

    public string? Author { get; init; }

    /// <summary>项目分类名，用于显示徽标。</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>CurseForge 页面地址。</summary>
    public string? WebsiteUrl { get; init; }

    /// <summary>最新文件的索引（搜索接口不返回完整文件对象）。</summary>
    public IReadOnlyList<CurseForgeFileIndex> LatestFiles { get; init; } = Array.Empty<CurseForgeFileIndex>();

    public string DownloadText => DownloadCount switch
    {
        >= 100_000_000 => $"{DownloadCount / 100_000_000.0:0.#} 亿",
        >= 10_000 => $"{DownloadCount / 10_000.0:0.#} 万",
        _ => DownloadCount.ToString(),
    };
}

/// <summary>搜索接口附带的文件索引。</summary>
public sealed class CurseForgeFileIndex
{
    public int FileId { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string GameVersion { get; init; } = string.Empty;

    public int ModLoader { get; init; }

    public int ReleaseType { get; init; }
}

/// <summary>CurseForge 的一个具体文件。</summary>
public sealed class CurseForgeFile
{
    public required int Id { get; init; }

    public int ModId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public required string FileName { get; init; }

    public string? DownloadUrl { get; init; }

    public long FileLength { get; init; } = -1;

    /// <summary>1=Release，2=Beta，3=Alpha。</summary>
    public int ReleaseType { get; init; }

    public DateTimeOffset FileDate { get; init; }

    public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();

    /// <summary>文件声明的前置模组。</summary>
    public IReadOnlyList<CurseForgeFileDependency> Dependencies { get; init; } =
        Array.Empty<CurseForgeFileDependency>();

    public string? Sha1 { get; init; }

    public bool IsRelease => ReleaseType == 1;
}

/// <summary>文件依赖项。<c>RelationType</c>：2=可选，3=必需，其余（内嵌/工具/不兼容）界面不展示。</summary>
public sealed class CurseForgeFileDependency
{
    public int ModId { get; init; }

    public int RelationType { get; init; }
}

/// <summary>一页搜索结果。</summary>
public sealed class CurseForgeSearchPage
{
    public IReadOnlyList<CurseForgeProject> Projects { get; init; } = Array.Empty<CurseForgeProject>();

    public int TotalCount { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

/// <summary>指纹匹配结果：本地文件对应哪个项目与哪个已发布文件。</summary>
public sealed class CurseForgeFingerprintMatch
{
    /// <summary>命中的本地文件指纹。</summary>
    public uint Fingerprint { get; init; }

    public required int ProjectId { get; init; }

    public required CurseForgeFile File { get; init; }

    public IReadOnlyList<CurseForgeFile> LatestFiles { get; init; } = Array.Empty<CurseForgeFile>();
}

/// <summary>本地资源对应的可更新版本。</summary>
public sealed class ResourceUpdateInfo
{
    public required LocalResource Resource { get; init; }

    public required CurseForgeFile File { get; init; }

    public string? CurrentVersionText { get; init; }

    public string LatestVersionText => string.IsNullOrWhiteSpace(File.DisplayName) ? File.FileName : File.DisplayName;
}
