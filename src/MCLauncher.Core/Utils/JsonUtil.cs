using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCLauncher.Core.Utils;

/// <summary>
/// Minecraft 生态的 JSON 有大量历史遗留：注释、尾随逗号、大小写不一致、
/// 甚至出现 NaN。这里统一放宽解析策略，模型侧用 JsonPropertyName 精确映射。
/// </summary>
public static class JsonUtil
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions WriteOptions = new(Options)
    {
        WriteIndented = true,
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T? DeserializeFromFile<T>(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Deserialize<T>(text);
    }

    public static string Serialize<T>(T value, bool indented = true) =>
        JsonSerializer.Serialize(value, indented ? WriteOptions : Options);

    /// <summary>原子写入：先写临时文件再替换，避免中途崩溃留下半个坏文件。</summary>
    public static void SerializeToFile<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(value), new UTF8Encoding(false));

        if (File.Exists(path))
        {
            File.Replace(temp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
