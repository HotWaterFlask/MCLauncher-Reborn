namespace MCLauncher.Core.Abstractions;

/// <summary>
/// 配置文件读写。实现必须做到"原子写 + 容错读"：
/// 半途崩溃不能留下坏配置，读到损坏内容要能回退到默认值而不是崩溃。
/// </summary>
public interface IConfigStore
{
    /// <summary>配置目录（如 %LOCALAPPDATA%\MCLauncher-Reborn）。</summary>
    string ConfigDirectory { get; }

    /// <summary>读取配置；文件不存在或损坏时返回 createDefault() 的结果。</summary>
    T Load<T>(string fileName, Func<T> createDefault) where T : class;

    /// <summary>原子写入配置。</summary>
    void Save<T>(string fileName, T value) where T : class;
}
