using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>
/// 一个已探测到的 Java 运行时。
/// <c>java.version</c> 可能是 4 段（<c>25.0.4.1</c>）、3 段（<c>17.0.1</c>）或旧式（<c>1.8.0_292</c>），
/// 这里统一归一成 主/次/补丁 三段 + 主版本号。
/// </summary>
public sealed class JavaInfo
{
    /// <summary>java 可执行文件全路径。</summary>
    public required string Path { get; init; }

    /// <summary>原始版本串（<c>java.version</c>）。</summary>
    public required string RawVersion { get; init; }

    /// <summary>归一后的主版本：<c>1.8.0_292</c> → 8，<c>25.0.4.1</c> → 25。</summary>
    public int MajorVersion { get; init; }

    public int MinorVersion { get; init; }

    public int PatchVersion { get; init; }

    /// <summary>额外段（4 段版本的第 4 位）。</summary>
    public int BuildVersion { get; init; }

    [JsonPropertyName("vendor")]
    public string? Vendor { get; init; }

    /// <summary><c>java.home</c>。</summary>
    public string? JavaHome { get; init; }

    /// <summary><c>os.arch</c>，如 <c>amd64</c> / <c>aarch64</c>。</summary>
    public string? Architecture { get; init; }

    /// <summary><c>sun.arch.data.model</c> 为 64，或 os.arch 明显是 64 位。</summary>
    public bool Is64Bit { get; init; }

    /// <summary><c>java.version.date</c>。</summary>
    public string? VersionDate { get; init; }

    /// <summary>排序键：主版本优先，其次次版本与补丁。</summary>
    public (int Major, int Minor, int Patch, int Build) SortKey =>
        (MajorVersion, MinorVersion, PatchVersion, BuildVersion);

    /// <summary>是否可用于启动要求 <paramref name="requiredMajor"/> 的版本。</summary>
    public bool CanRun(int requiredMajor) => MajorVersion >= requiredMajor;

    public override string ToString() => $"Java {RawVersion} ({(Is64Bit ? "64" : "32")}位) @ {Path}";
}
