using System.Text.Json.Serialization;

namespace MCLauncher.Core.Services;

/// <summary>缓存下来的 Java 探测结果，避免每次启动都重新跑一遍进程探测。</summary>
public sealed class DetectedJava
{
    public string Path { get; set; } = string.Empty;

    public string RawVersion { get; set; } = string.Empty;

    public int MajorVersion { get; set; }

    public bool Is64Bit { get; set; } = true;

    public string? Vendor { get; set; }

    [JsonIgnore]
    public string Display => $"Java {RawVersion}（{(Is64Bit ? "64" : "32")}位）{(string.IsNullOrWhiteSpace(Vendor) ? "" : " · " + Vendor)}";
}
