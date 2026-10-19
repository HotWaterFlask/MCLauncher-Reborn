using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>一个资源对象。<c>assets/objects/&lt;hash 前两位&gt;/&lt;hash&gt;</c> 是固定磁盘布局。</summary>
public sealed class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>相对路径：<c>ab/abcdef…</c>。</summary>
    [JsonIgnore]
    public string RelativePath => Hash.Length >= 2 ? $"{Hash[..2]}/{Hash}" : Hash;
}

/// <summary>
/// 资源索引文件（<c>assets/indexes/&lt;id&gt;.json</c>）。
/// <para>
/// **注意**：<c>virtual</c> 与 <c>map_to_resources</c> 这两个标记在**索引文件**里，
/// 不在版本 JSON 的 <c>assetIndex</c> 对象里（实测 1.7.10 的索引两者都没有，
/// 而 <c>legacy.json</c> 有 <c>virtual:true</c>、<c>pre-1.6.json</c> 有 <c>map_to_resources:true</c>）。
/// 漏了这两个标记会让远古版本贴图全丢。
/// </para>
/// </summary>
public sealed class AssetIndex
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = new();

    /// <summary>为真时 objects 要展开到 <c>assets/virtual/&lt;id&gt;/&lt;原名&gt;</c>，并把 <c>assets_root</c> 指过去。</summary>
    [JsonPropertyName("virtual")]
    public bool Virtual { get; set; }

    /// <summary>为真时 objects 还要落到 <c>&lt;游戏目录&gt;/resources/&lt;原名&gt;</c>。</summary>
    [JsonPropertyName("map_to_resources")]
    public bool MapToResources { get; set; }
}
