using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>本地资源类别。</summary>
public enum ResourceKind
{
    Mod,
    ResourcePack,
    ShaderPack,
    World,
    Schematic,
    Datapack,
}

/// <summary>扫描出来的一个本地资源。</summary>
public sealed class LocalResource
{
    public required ResourceKind Kind { get; init; }

    /// <summary>文件或目录的完整路径（启用态切换时会跟着改名，因此可写）。</summary>
    public required string FilePath { get; set; }

    public required string FileName { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>是否启用（禁用的 Mod / 资源包以 <c>.disabled</c> 结尾）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>从元数据里读出的展示名；读不到时用文件名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? Description { get; set; }

    /// <summary>资源声明的目标游戏版本（存档为存档版本，Mod 为加载器/MC 版本）。</summary>
    public string? GameVersion { get; set; }

    /// <summary>Mod 使用的加载器（Fabric / Forge / NeoForge / Quilt），其它资源为 null。</summary>
    public string? Loader { get; set; }

    /// <summary>列表里显示的图标（Mod logo / 资源包 pack.png / 存档 icon.png）。</summary>
    [JsonIgnore]
    public byte[]? IconBytes { get; set; }

    /// <summary>大小的人类可读形式。</summary>
    [JsonIgnore]
    public string SizeText => SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024 * 1024):0.00} GB",
        >= 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{SizeBytes / 1024.0:0.0} KB",
        _ => $"{SizeBytes} B",
    };

    [JsonIgnore]
    public string ModifyText => ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>列表里显示的标题。</summary>
    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName!;

    public override string ToString() => $"{Kind}: {Title}";
}
