using System.IO.Compression;

namespace MCLauncher.Core.Utils;

/// <summary>
/// 极简 NBT 读取器，只服务于"读存档名与版本"这一个用途。
/// <para>
/// 做法是流式跳过不需要的标签，只把 <c>Data/LevelName</c> 与 <c>Data/Version/Name</c> 取出来，
/// 因此不需要建立完整对象树 —— 存档的 level.dat 动辄几十 KB，全量建树既慢又没必要。
/// </para>
/// <para>level.dat 是 **gzip 压缩** 的 NBT，需要先识别魔数再解压。</para>
/// </summary>
public static class NbtReader
{
    private static readonly byte[] GzipMagic = { 0x1F, 0x8B };

    /// <summary>读取存档的显示名与游戏版本；失败时返回 false（损坏的存档不应让界面崩掉）。</summary>
    public static bool TryReadLevelDat(string levelDatPath, out string? levelName, out string? versionName)
    {
        levelName = null;
        versionName = null;

        if (!File.Exists(levelDatPath))
        {
            return false;
        }

        try
        {
            using var file = File.OpenRead(levelDatPath);
            using var buffered = new BufferedStream(file);

            Stream stream = buffered;
            GZipStream? gzip = null;

            var header = new byte[2];
            if (buffered.Read(header, 0, 2) == 2
                && header[0] == GzipMagic[0] && header[1] == GzipMagic[1])
            {
                buffered.Seek(0, SeekOrigin.Begin);
                gzip = new GZipStream(buffered, CompressionMode.Decompress);
                stream = gzip;
            }
            else
            {
                buffered.Seek(0, SeekOrigin.Begin);
            }

            try
            {
                var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);

                var rootType = reader.ReadByte();
                if (rootType != TagCompound)
                {
                    return false;
                }

                _ = ReadString(reader); // 根标签名（通常是空串）
                return ReadCompound(reader, string.Empty, ref levelName, ref versionName);
            }
            finally
            {
                gzip?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }

    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagShort = 2;
    private const byte TagInt = 3;
    private const byte TagLong = 4;
    private const byte TagFloat = 5;
    private const byte TagDouble = 6;
    private const byte TagByteArray = 7;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagIntArray = 11;
    private const byte TagLongArray = 12;

    private static bool ReadCompound(
        BinaryReader reader,
        string prefix,
        ref string? levelName,
        ref string? versionName)
    {
        while (true)
        {
            var type = reader.ReadByte();
            if (type == TagEnd)
            {
                return levelName is not null || versionName is not null;
            }

            var name = ReadString(reader);
            var path = prefix.Length == 0 ? name : $"{prefix}/{name}";

            if (type == TagString && (path is "Data/LevelName" or "LevelName"))
            {
                levelName = ReadString(reader);
                continue;
            }

            if (type == TagCompound)
            {
                ReadCompound(reader, path, ref levelName, ref versionName);
                continue;
            }

            if (type == TagString && (path is "Data/Version/Name" or "Version/Name"))
            {
                versionName = ReadString(reader);
                continue;
            }

            if (!Skip(reader, type))
            {
                return false;
            }
        }
    }

    /// <summary>跳过指定类型的负载。返回 false 表示数据已损坏。</summary>
    private static bool Skip(BinaryReader reader, byte type)
    {
        switch (type)
        {
            case TagByte:
                reader.ReadByte();
                return true;

            case TagShort:
                reader.ReadInt16();
                return true;

            case TagInt:
            case TagFloat:
                reader.ReadInt32();
                return true;

            case TagLong:
            case TagDouble:
                reader.ReadInt64();
                return true;

            case TagByteArray:
                reader.BaseStream.Seek(reader.ReadInt32(), SeekOrigin.Current);
                return true;

            case TagString:
                SeekString(reader);
                return true;

            case TagList:
                {
                    var elementType = reader.ReadByte();
                    var length = reader.ReadInt32();
                    if (length < 0)
                    {
                        return false;
                    }

                    for (var i = 0; i < length; i++)
                    {
                        if (elementType == TagEnd)
                        {
                            // TAG_End 列表（长度通常为 0），无需再读。
                            break;
                        }

                        if (elementType == TagCompound)
                        {
                            if (!SkipCompound(reader))
                            {
                                return false;
                            }
                        }
                        else if (!Skip(reader, elementType))
                        {
                            return false;
                        }
                    }

                    return true;
                }

            case TagCompound:
                return SkipCompound(reader);

            case TagIntArray:
                reader.BaseStream.Seek(reader.ReadInt32() * 4L, SeekOrigin.Current);
                return true;

            case TagLongArray:
                reader.BaseStream.Seek(reader.ReadInt32() * 8L, SeekOrigin.Current);
                return true;

            default:
                return false;
        }
    }

    private static bool SkipCompound(BinaryReader reader)
    {
        while (true)
        {
            var type = reader.ReadByte();
            if (type == TagEnd)
            {
                return true;
            }

            SeekString(reader); // 标签名
            if (!Skip(reader, type))
            {
                return false;
            }
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = reader.ReadBytes(length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static void SeekString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        reader.BaseStream.Seek(length, SeekOrigin.Current);
    }
}
