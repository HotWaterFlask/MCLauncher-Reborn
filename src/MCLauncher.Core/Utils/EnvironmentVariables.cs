namespace MCLauncher.Core.Utils;

/// <summary>
/// 把设置页里的「名称=值」多行文本解析成环境变量表。
/// 空行与 <c>#</c> 开头的注释会被跳过；值两端的双引号会被去掉。
/// </summary>
public static class EnvironmentVariables
{
    public static Dictionary<string, string> Parse(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (name.Length == 0 || name.Contains('\0'))
            {
                continue;
            }

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            result[name] = value;
        }

        return result;
    }
}
