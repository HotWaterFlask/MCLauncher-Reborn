using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace MCLauncher.Core.Utils;

/// <summary>
/// MurmurHash2（32 位）及其 CurseForge 文件指纹变体。
/// <para>
/// CurseForge 的指纹规则是：把文件里所有空白字节（0x09 / 0x0A / 0x0D / 0x20）剔除，
/// 再对剩余内容做 seed=1 的 MurmurHash2。这里分两遍读文件：第一遍统计长度，第二遍流式计算，
/// 避免把几十 MB 的 jar 整个读进内存。
/// </para>
/// </summary>
public static class MurmurHash2
{
    private const uint M = 0x5bd1e995;
    private const int R = 24;
    private const uint DefaultSeed = 1;

    /// <summary>标准 MurmurHash2 32 位哈希。</summary>
    public static uint Hash32(ReadOnlySpan<byte> data, uint seed = DefaultSeed)
    {
        var h = seed ^ (uint)data.Length;
        var index = 0;

        while (data.Length - index >= 4)
        {
            var k = BinaryPrimitives.ReadUInt32LittleEndian(data[index..]);
            k *= M;
            k ^= k >> R;
            k *= M;

            h *= M;
            h ^= k;
            index += 4;
        }

        switch (data.Length - index)
        {
            case 3:
                h ^= (uint)data[index + 2] << 16;
                goto case 2;
            case 2:
                h ^= (uint)data[index + 1] << 8;
                goto case 1;
            case 1:
                h ^= data[index];
                h *= M;
                break;
        }

        h ^= h >> 13;
        h *= M;
        h ^= h >> 15;
        return h;
    }

    /// <summary>计算 CurseForge 文件指纹；文件读不到时抛出 IO 异常，由调用方决定是否跳过。</summary>
    public static uint ComputeFileFingerprint(string filePath)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var length = CountFilteredLength(filePath, buffer);

            using var stream = File.OpenRead(filePath);
            var h = DefaultSeed ^ length;
            uint k = 0;
            var kCount = 0;

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var value = buffer[i];
                    if (IsWhitespace(value))
                    {
                        continue;
                    }

                    k |= (uint)value << (8 * kCount);
                    if (++kCount < 4)
                    {
                        continue;
                    }

                    k *= M;
                    k ^= k >> R;
                    k *= M;
                    h *= M;
                    h ^= k;

                    k = 0;
                    kCount = 0;
                }
            }

            if (kCount > 0)
            {
                h ^= k;
                h *= M;
            }

            h ^= h >> 13;
            h *= M;
            h ^= h >> 15;
            return h;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static uint CountFilteredLength(string filePath, byte[] buffer)
    {
        using var stream = File.OpenRead(filePath);
        uint length = 0;

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (!IsWhitespace(buffer[i]))
                {
                    length++;
                }
            }
        }

        return length;
    }

    private static bool IsWhitespace(byte value) => value is 0x09 or 0x0A or 0x0D or 0x20;
}
