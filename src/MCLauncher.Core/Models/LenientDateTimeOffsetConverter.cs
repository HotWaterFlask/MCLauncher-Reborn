using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>
/// 宽松的 <see cref="DateTimeOffset"/> 转换器。
/// <para>
/// 上游 JSON 里的时间字段并不总是 ISO-8601：Fabric 的 profile JSON 就出现过解析不出时间戳的
/// <c>releaseTime</c>，直接让整份版本 JSON 反序列化失败。时间字段本来只是展示用，
/// 解析不出来就按 null 处理，绝不能因为它把版本 JSON 整个废掉。
/// </para>
/// </summary>
public sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed) ? parsed : null;

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var unixSeconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                    : null;

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
