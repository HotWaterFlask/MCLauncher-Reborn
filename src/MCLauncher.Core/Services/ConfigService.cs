using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Secrets;

namespace MCLauncher.Core.Services;

/// <summary>
/// 设置读写与凭据解析。凭据采用三级覆盖：配置文件 &gt; 环境变量 &gt; 空值。
/// 空值时上层应给出引导，而不是抛异常。
/// </summary>
public sealed class ConfigService
{
    private const string SettingsFileName = "settings.json";

    private readonly IConfigStore _store;
    private readonly ILogger _log;
    private readonly bool _includeBuiltInSecrets;

    /// <param name="includeBuiltInSecrets">
    /// 是否允许回退到 <c>Secrets\LocalSecrets.cs</c> 里的本机默认凭据。
    /// 测试可关掉它，避免受开发者本地凭据影响。
    /// </param>
    public ConfigService(IConfigStore store, ILogger log, bool includeBuiltInSecrets = true)
    {
        _store = store;
        _log = log;
        _includeBuiltInSecrets = includeBuiltInSecrets;
    }

    public AppSettings Settings { get; private set; } = new();

    /// <summary>本次启动前没有 settings.json，也就是第一次使用启动器。</summary>
    public bool IsFirstRun { get; private set; }

    public string ConfigDirectory => _store.ConfigDirectory;

    /// <summary>日志目录（与配置目录同级）。</summary>
    public string LogDirectory => Path.Combine(_store.ConfigDirectory, "logs");

    public void Load()
    {
        var path = Path.Combine(_store.ConfigDirectory, SettingsFileName);
        IsFirstRun = !File.Exists(path);

        Settings = _store.Load(SettingsFileName, () => new AppSettings());
        _log.Info($"已加载设置：{path}");
    }

    /// <summary>保存设置；返回 false 表示落盘失败，调用方应提示用户而不是显示「已保存」。</summary>
    public bool Save()
    {
        return _store.Save(SettingsFileName, Settings);
    }

    /// <summary>
    /// 解析微软客户端 ID：配置 &gt; 环境变量 MCLR_MS_CLIENT_ID &gt; 代码内的 LocalSecrets &gt; null。
    /// </summary>
    public string? ResolveMsClientId()
    {
        if (!string.IsNullOrWhiteSpace(Settings.MsClientId))
        {
            return Settings.MsClientId!.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable("MCLR_MS_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        // 开发者在 Secrets\LocalSecrets.cs 里写的默认值（该文件不入库）。
        return _includeBuiltInSecrets && !string.IsNullOrWhiteSpace(LocalSecrets.MsClientId)
            ? LocalSecrets.MsClientId.Trim()
            : null;
    }

    /// <summary>
    /// 解析 CurseForge API Key：配置 &gt; 环境变量 MCLR_CURSEFORGE_API_KEY &gt; 代码内的 LocalSecrets &gt; null。
    /// </summary>
    public string? ResolveCurseForgeApiKey()
    {
        if (!string.IsNullOrWhiteSpace(Settings.CurseForgeApiKey))
        {
            return Settings.CurseForgeApiKey!.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable("MCLR_CURSEFORGE_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        return _includeBuiltInSecrets && !string.IsNullOrWhiteSpace(LocalSecrets.CurseForgeApiKey)
            ? LocalSecrets.CurseForgeApiKey.Trim()
            : null;
    }

    /// <summary>
    /// 把启动器自己的 <c>.minecraft</c> 与官方启动器的 <c>%APPDATA%\.minecraft</c> 都登记到目录列表里。
    /// 官方目录不存在时跳过，不制造死条目。
    /// </summary>
    public void EnsureKnownGameFolders(string launcherDirectory)
    {
        EnsureDefaultGameFolder(launcherDirectory);

        var official = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

        if (!Directory.Exists(official) ||
            Settings.GameFolders.Any(folder => SameFolder(folder, official)))
        {
            return;
        }

        Settings.GameFolders.Add(Path.GetFullPath(official).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>确保默认游戏目录（启动器所在目录下的 .minecraft）存在并登记，目录缺失时直接创建。</summary>
    public string EnsureDefaultGameFolder(string launcherDirectory)
    {
        var candidate = Path.Combine(launcherDirectory, ".minecraft");
        if (!Settings.GameFolders.Any(p => string.Equals(
                Path.GetFullPath(p).TrimEnd('\\'),
                Path.GetFullPath(candidate).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase)))
        {
            Settings.GameFolders.Add(candidate);
        }

        Settings.SelectedGameFolder ??= candidate;

        // 首次运行 / 用户删掉目录后启动：把 .minecraft 建出来，避免实例与目录功能全都落空。
        try
        {
            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                _log.Info($"已创建默认游戏目录：{candidate}");
            }

            if (!Directory.Exists(Settings.SelectedGameFolder))
            {
                Directory.CreateDirectory(Settings.SelectedGameFolder);
                _log.Info($"已创建游戏目录：{Settings.SelectedGameFolder}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            _log.Warn($"创建游戏目录失败：{Settings.SelectedGameFolder}", ex);
        }

        return Settings.SelectedGameFolder;
    }
}
