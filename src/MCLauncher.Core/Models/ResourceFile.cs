namespace MCLauncher.Core.Models;

/// <summary>
/// 下载服务需要的文件描述，与具体平台（CurseForge / Modrinth）无关。
/// </summary>
public sealed class ResourceFile
{
    public required string FileName { get; init; }

    /// <summary>展示用名称，缺省时用文件名。</summary>
    public string? DisplayName { get; init; }

    public string? DownloadUrl { get; init; }

    /// <summary>SHA-1，用于下载完成后校验。</summary>
    public string? Sha1 { get; init; }

    /// <summary>文件字节数；未知时为 -1。</summary>
    public long FileLength { get; init; } = -1;

    public static ResourceFile FromCurseForge(CurseForgeFile file) => new()
    {
        FileName = file.FileName,
        DisplayName = file.DisplayName,
        DownloadUrl = file.DownloadUrl,
        Sha1 = file.Sha1,
        FileLength = file.FileLength,
    };
}
