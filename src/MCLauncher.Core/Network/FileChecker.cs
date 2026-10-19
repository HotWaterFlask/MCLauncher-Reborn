using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Network;

/// <summary>
/// 文件校验规则：大小 + SHA-1。Minecraft 全链路都是 SHA-1。
/// 只有哈希时以哈希为准，只有大小时以大小为准，都没有则视为「存在即可用」。
/// </summary>
public sealed class FileChecker
{
    /// <summary>期望的 SHA-1（小写十六进制），null/空表示不校验。</summary>
    public string? Sha1 { get; init; }

    /// <summary>期望的字节数，负数表示未知（不校验）。</summary>
    public long Size { get; init; } = -1;

    public static FileChecker Of(string? sha1, long size = -1) => new() { Sha1 = sha1, Size = size };

    /// <summary>仅按大小校验。</summary>
    public static FileChecker OfSize(long size) => new() { Size = size };

    /// <summary>存在即通过。</summary>
    public static FileChecker ExistenceOnly { get; } = new();

    /// <summary>该规则是否有实际约束（无约束时可直接跳过下载判断）。</summary>
    public bool HasConstraint => !string.IsNullOrWhiteSpace(Sha1) || Size >= 0;

    /// <summary>校验指定文件是否符合本规则。</summary>
    public bool Check(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (Size >= 0 && info.Length != Size)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(Sha1) || HashUtil.VerifySha1(path, Sha1);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public override string ToString()
    {
        var hash = string.IsNullOrWhiteSpace(Sha1) ? "无哈希" : Sha1![..Math.Min(8, Sha1!.Length)];
        var size = Size >= 0 ? Size.ToString() : "未知大小";
        return $"{hash}/{size}";
    }
}
