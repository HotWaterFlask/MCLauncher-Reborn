using System.IO.Compression;
using System.Text;

namespace MCLauncher.Core.Tests.Support;

/// <summary>在测试里造压缩包（带目录结构的条目）。</summary>
internal static class ZipBuilder
{
    public static void Create(string path, params (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }

    /// <summary>造一个 gzip 压缩的 level.dat（最小可用的 NBT）。</summary>
    public static void WriteLevelDat(string path, string levelName, string versionName)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        using var writer = new BinaryWriter(gzip, Encoding.UTF8);

        writer.Write((byte)10); // TAG_Compound
        WriteString(writer, string.Empty);

        writer.Write((byte)10); // Data
        WriteString(writer, "Data");

        writer.Write((byte)8); // LevelName
        WriteString(writer, "LevelName");
        WriteString(writer, levelName);

        writer.Write((byte)10); // Version
        WriteString(writer, "Version");

        writer.Write((byte)8); // Name
        WriteString(writer, "Name");
        WriteString(writer, versionName);

        writer.Write((byte)0); // End of Version
        writer.Write((byte)0); // End of Data
        writer.Write((byte)0); // End of root
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((byte)(bytes.Length >> 8));
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }
}
