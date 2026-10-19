using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>受管文件记录：相对游戏目录的路径 + 校验信息。</summary>
public sealed class ManagedFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; } = -1;
}

/// <summary>资源对象的快照：只记录索引名与数量，3000 多个对象不逐个登记。</summary>
public sealed class ManagedAssets
{
    [JsonPropertyName("indexId")]
    public string IndexId { get; set; } = string.Empty;

    [JsonPropertyName("objectCount")]
    public int ObjectCount { get; set; }
}

/// <summary>
/// <c>&lt;游戏目录&gt;/.mclauncher/manifest.json</c>：本启动器安装过的受管文件清单。
/// 用于"校验并修复"与卸载，不依赖上游版本 JSON 是否还在。
/// </summary>
public sealed class ManagedManifest
{
    public const int CurrentFormatVersion = 1;

    [JsonPropertyName("format")]
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    /// <summary>该版本继承的原版基底 id（原版版本为 null）。</summary>
    [JsonPropertyName("baseVersionId")]
    public string? BaseVersionId { get; set; }

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("files")]
    public List<ManagedFile> Files { get; set; } = new();

    [JsonPropertyName("assets")]
    public ManagedAssets? Assets { get; set; }
}
