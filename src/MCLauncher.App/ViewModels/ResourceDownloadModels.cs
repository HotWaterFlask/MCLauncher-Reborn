using System.Windows.Input;
using System.Windows.Media;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;

namespace MCLauncher.App.ViewModels;

/// <summary>安装源下拉项。</summary>
public sealed class DownloadSourceOption
{
    public required ResourceSource Source { get; init; }

    public required string Label { get; init; }

    public override string ToString() => Label;
}

/// <summary>排序下拉项；两个平台的取值不同，按当前源选用。</summary>
public sealed class DownloadSortOption
{
    public required string Label { get; init; }

    public CurseForgeSort CurseForgeSort { get; init; }

    public string ModrinthIndex { get; init; } = "relevance";

    public override string ToString() => Label;
}

/// <summary>下载列表里的一行项目（CurseForge / Modrinth 共用一个视图）。</summary>
public sealed class DownloadProjectRow : ViewModelBase
{
    private ImageSource? _icon;

    public required string Name { get; init; }

    public required string Summary { get; init; }

    public required string DownloadText { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    public string? Author { get; init; }

    public string? IconUrl { get; init; }

    /// <summary>CurseForge 项目 id；Modrinth 项目为 0。</summary>
    public int CurseForgeId { get; init; }

    /// <summary>Modrinth 项目 id；CurseForge 项目为 null。</summary>
    public string? ModrinthId { get; init; }

    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);

    public string AuthorText => "作者：" + Author;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon is not null;

    public static DownloadProjectRow FromCurseForge(CurseForgeProject project) => new()
    {
        Name = project.Name,
        Summary = project.Summary,
        DownloadText = project.DownloadCount == 0 ? string.Empty : project.DownloadText + " 次下载",
        Categories = project.Categories,
        Author = project.Author,
        IconUrl = project.LogoUrl,
        CurseForgeId = project.Id,
    };

    public static DownloadProjectRow FromModrinth(ModrinthProject project) => new()
    {
        Name = project.Name,
        Summary = project.Summary,
        DownloadText = project.DownloadCount == 0 ? string.Empty : project.DownloadText + " 次下载",
        Categories = project.Categories,
        Author = project.Author,
        IconUrl = project.IconUrl,
        ModrinthId = project.Id,
    };
}

/// <summary>项目详情页里的一个版本。</summary>
public sealed class DownloadVersionRow
{
    public required ResourceVersionInfo Version { get; init; }

    public string Name => string.IsNullOrWhiteSpace(Version.Name) ? Version.File.FileName : Version.Name;

    public string DateText => Version.PublishedAt.ToLocalTime().ToString("yyyy年MM月dd日 HH:mm");

    public string TypeText => Version.VersionType.Trim().ToLowerInvariant() switch
    {
        "beta" => "测试版本",
        "alpha" => "预览版本",
        _ => "稳定版本",
    };

    public string LoaderText => string.Join("  ", Version.Loaders.Select(FormatLoader));

    public bool HasLoaderText => Version.Loaders.Count > 0;

    public bool IsStable => Version.IsRelease;

    private static string FormatLoader(string name) => name.Trim().ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        "liteloader" => "LiteLoader",
        "rift" => "Rift",
        _ => name,
    };
}

/// <summary>按 Minecraft 版本折叠的一组版本。</summary>
public sealed class DownloadVersionGroup : ViewModelBase
{
    private bool _isExpanded;

    public required string Header { get; init; }

    public required string CountText { get; init; }

    public required IReadOnlyList<DownloadVersionRow> Versions { get; init; }

    public required ICommand ToggleCommand { get; init; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ChevronGlyph));
            }
        }
    }

    public string ChevronGlyph => IsExpanded ? "\uE70D" : "\uE76C";
}

/// <summary>下载弹窗里的一条前置内容。</summary>
public sealed class DownloadDependencyRow : ViewModelBase
{
    private ImageSource? _icon;

    public required ResourceDependency Dependency { get; init; }

    public string Name => Dependency.Project?.Name ?? FallbackName;

    public string Summary => string.IsNullOrWhiteSpace(Dependency.Project?.Summary)
        ? "未能获取项目信息，可直接到资源站查找"
        : Dependency.Project!.Summary;

    public IReadOnlyList<string> Categories => Dependency.Project?.Categories ?? Array.Empty<string>();

    public string? IconUrl => Dependency.Project?.IconUrl;

    public string FallbackName => Dependency.ModrinthProjectId is { Length: > 0 } id
        ? "项目 " + id
        : "项目 #" + Dependency.CurseForgeProjectId;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon is not null;
}
