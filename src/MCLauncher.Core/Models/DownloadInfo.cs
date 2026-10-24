using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>
/// 一个可下载文件的描述（URL + 校验信息）。
/// 出现在版本 JSON 的 <c>downloads</c>、<c>libraries[].downloads.artifact</c>、
/// <c>assetIndex</c> 等所有位置，形状高度一致。
/// </summary>
public class DownloadInfo
{
    /// <summary>制品相对 <c>libraries</c> 的路径；安装了分类器或非 jar 扩展名时以它为准。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; } = -1;

    /// <summary>是否带有可用的下载地址。</summary>
    [JsonIgnore]
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
}
