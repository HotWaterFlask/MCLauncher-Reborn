using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>
/// 实例（版本）级配置，存放在 <c>&lt;游戏目录&gt;/versions/&lt;id&gt;/.mclauncher/instance.json</c>。
/// 「版本隔离」开启时，mods / 存档 / 配置都在 <c>versions/&lt;id&gt;</c> 下，
/// 与原版共享的 <c>libraries</c>、<c>assets</c> 分开 —— 这样多个版本互不污染。
/// </summary>
public sealed class InstanceConfig
{
    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    /// <summary>是否启用版本隔离；null 表示跟随全局设置。</summary>
    [JsonPropertyName("isolated")]
    public bool? Isolated { get; set; }

    /// <summary>界面展示名（可自定义，留空则用版本 id）。</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>该实例安装过的加载器描述（Forge 49.0.30 / Fabric 0.15.3 …）。</summary>
    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = new();
}
