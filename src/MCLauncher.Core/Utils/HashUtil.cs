using System.Security.Cryptography;

namespace MCLauncher.Core.Utils;

/// <summary>SHA-1 计算与校验。Minecraft 全链路（客户端 jar、库、资源对象）都用 SHA-1。</summary>
public static class HashUtil
{
    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = "0123456789abcdef"[b >> 4];
            chars[(i * 2) + 1] = "0123456789abcdef"[b & 0xF];
        }

        return new string(chars);
    }

    public static string Sha1OfBytes(byte[] data) => ToHex(SHA1.HashData(data));

    public static string Sha1OfString(string text) => Sha1OfBytes(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>流式计算文件 SHA-1，避免把大文件整个读进内存。</summary>
    public static string Sha1OfFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return ToHex(SHA1.HashData(stream));
    }

    /// <summary>校验文件 SHA-1，期望值为空时直接视为通过。</summary>
    public static bool VerifySha1(string path, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(Sha1OfFile(path), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>流式计算文件 SHA-256（Zulu 等第三方安装包用）。</summary>
    public static string Sha256OfFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return ToHex(SHA256.HashData(stream));
    }

    /// <summary>校验文件 SHA-256，期望值为空时直接视为通过。</summary>
    public static bool VerifySha256(string path, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(Sha256OfFile(path), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>计算字符串的 MD5（离线账号 UUID 需要）。</summary>
    public static byte[] Md5OfBytes(byte[] data) => MD5.HashData(data);

    public static string Md5ToHex(byte[] data) => ToHex(MD5.HashData(data));

    /// <summary>
    /// 离线账号 UUID。协议规定为
    /// <c>UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))</c>：
    /// MD5 之后要把第 7 字节的版本位置成 3、第 9 字节的变体位成 RFC 4122，
    /// 否则生成的是"像 UUID 但不是 UUID"的值，联机会被判定为非法会话。
    /// </summary>
    public static string OfflinePlayerUuid(string name)
    {
        var bytes = MD5.HashData(System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30); // version 3（MD5 命名空间）
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // IETF variant
        return ToHex(bytes);
    }

    /// <summary>把 32 位无横杠的 UUID 串格式化成带横杠的标准写法。</summary>
    public static string FormatUuid(string uuid)
    {
        var raw = uuid.Replace("-", string.Empty);
        if (raw.Length != 32)
        {
            return uuid;
        }

        return $"{raw[..8]}-{raw[8..12]}-{raw[12..16]}-{raw[16..20]}-{raw[20..]}";
    }
}
