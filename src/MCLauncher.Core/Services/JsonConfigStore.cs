using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>基于 JSON 文件的配置存储。</summary>
public sealed class JsonConfigStore : IConfigStore
{
    private readonly ILogger _log;

    public JsonConfigStore(string configDirectory, ILogger log)
    {
        ConfigDirectory = configDirectory;
        _log = log;
        Directory.CreateDirectory(configDirectory);
    }

    public string ConfigDirectory { get; }

    public T Load<T>(string fileName, Func<T> createDefault) where T : class
    {
        var path = Path.Combine(ConfigDirectory, fileName);
        if (!File.Exists(path))
        {
            return createDefault();
        }

        try
        {
            var value = JsonUtil.DeserializeFromFile<T>(path);
            return value ?? createDefault();
        }
        catch (Exception ex)
        {
            _log.Warn($"配置文件解析失败，已回退到默认值：{path}", ex);
            return createDefault();
        }
    }

    public void Save<T>(string fileName, T value) where T : class
    {
        var path = Path.Combine(ConfigDirectory, fileName);
        try
        {
            JsonUtil.SerializeToFile(path, value);
        }
        catch (Exception ex)
        {
            _log.Error($"配置文件写入失败：{path}", ex);
        }
    }
}
