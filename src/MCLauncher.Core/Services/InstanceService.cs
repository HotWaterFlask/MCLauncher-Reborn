using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>
/// 实例（版本）管理：隔离开关、展示名、加载器记录、游戏目录列表与实例的创建/删除。
/// </summary>
public sealed class InstanceService
{
    private const string InstanceFileName = "instance.json";

    private readonly VersionRepository _repository;
    private readonly ILogger _log;

    public InstanceService(VersionRepository repository, ILogger log)
    {
        _repository = repository;
        _log = log;
    }

    /// <summary>实例配置文件路径。</summary>
    public static string ConfigPath(string gameDirectory, string versionId) =>
        Path.Combine(VersionRepository.VersionDirectory(gameDirectory, versionId), ".mclauncher", InstanceFileName);

    /// <summary>读取实例配置；不存在时返回一个默认配置（不落盘）。</summary>
    public InstanceConfig Load(string gameDirectory, string versionId)
    {
        var path = ConfigPath(gameDirectory, versionId);
        if (!File.Exists(path))
        {
            return new InstanceConfig { VersionId = versionId };
        }

        try
        {
            var config = JsonUtil.DeserializeFromFile<InstanceConfig>(path);
            if (config is null)
            {
                return new InstanceConfig { VersionId = versionId };
            }

            config.VersionId = versionId;
            return config;
        }
        catch (Exception ex)
        {
            _log.Warn($"实例配置解析失败，已回退默认值：{path}", ex);
            return new InstanceConfig { VersionId = versionId };
        }
    }

    public void Save(string gameDirectory, string versionId, InstanceConfig config)
    {
        config.VersionId = versionId;
        JsonUtil.SerializeToFile(ConfigPath(gameDirectory, versionId), config);
    }

    /// <summary>该实例是否启用隔离；未显式设置时跟随全局默认。</summary>
    public bool IsIsolated(string gameDirectory, string versionId, bool globalDefault)
    {
        var config = Load(gameDirectory, versionId);
        return config.Isolated ?? globalDefault;
    }

    public void SetIsolation(string gameDirectory, string versionId, bool? isolated)
    {
        var config = Load(gameDirectory, versionId);
        config.Isolated = isolated;
        Save(gameDirectory, versionId, config);
        _log.Info($"实例 {versionId} 的版本隔离设为：{(isolated is null ? "跟随全局" : isolated.Value ? "开" : "关")}");
    }

    /// <summary>实例的游戏目录：隔离时为 <c>versions/&lt;id&gt;</c>，否则为游戏根目录。</summary>
    public string GetGameDirectory(string gameDirectory, string versionId, bool globalDefault)
    {
        var directory = IsIsolated(gameDirectory, versionId, globalDefault)
            ? VersionRepository.VersionDirectory(gameDirectory, versionId)
            : gameDirectory;

        PathUtil.EnsureDirectory(directory);
        return directory;
    }

    /// <summary>隔离模式下游戏会读写的子目录（用于界面提示与备份）。</summary>
    public static IReadOnlyList<string> InstanceSubDirectories { get; } = new[]
    {
        "mods",
        "resourcepacks",
        "shaderpacks",
        "saves",
        "schematics",
        "config",
        "logs",
        "crash-reports",
    };

    /// <summary>确保隔离目录结构与主游戏目录一致（首次开启隔离时调用）。</summary>
    public void EnsureIsolatedLayout(string gameDirectory, string versionId)
    {
        var instanceDirectory = VersionRepository.VersionDirectory(gameDirectory, versionId);
        PathUtil.EnsureDirectory(instanceDirectory);

        foreach (var sub in InstanceSubDirectories)
        {
            PathUtil.EnsureDirectory(Path.Combine(instanceDirectory, sub));
        }

        _log.Trace($"已准备隔离目录：{instanceDirectory}");
    }

    /// <summary>记录该实例安装过的加载器。</summary>
    public void AddLoader(string gameDirectory, string versionId, string loaderDescription)
    {
        var config = Load(gameDirectory, versionId);
        if (!config.Loaders.Contains(loaderDescription, StringComparer.OrdinalIgnoreCase))
        {
            config.Loaders.Add(loaderDescription);
            Save(gameDirectory, versionId, config);
        }
    }

    /// <summary>删除实例（含隔离目录与配置）。</summary>
    public bool Delete(string gameDirectory, string versionId) => _repository.Delete(gameDirectory, versionId);

    // ------------------------------------------------------------------ 创建实例

    /// <summary>
    /// 把用户输入的实例名规整成合法且不冲突的目录名：替换非法字符，冲突时追加 (2)、(3)。
    /// </summary>
    public string CreateUniqueInstanceId(string gameDirectory, string? desiredName, string fallbackName)
    {
        var name = string.IsNullOrWhiteSpace(desiredName) ? fallbackName : desiredName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        name = builder.ToString().Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            name = fallbackName;
        }

        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(VersionRepository.VersionDirectory(gameDirectory, candidate)))
        {
            candidate = $"{name} ({suffix++})";
        }

        return candidate;
    }

    /// <summary>
    /// 创建一个「只继承原版」的空实例：不复制客户端 jar，<c>jar</c> 指回基底版本。
    /// 这样同一个原版可以开多个互不影响（mods / 存档隔离）的实例。
    /// </summary>
    public string CreateProfileInstance(
        string gameDirectory,
        string instanceId,
        string baseVersionId,
        string? displayName = null,
        bool isolated = true)
    {
        if (_repository.IsInstalled(gameDirectory, instanceId))
        {
            throw new InvalidOperationException($"实例 {instanceId} 已经存在");
        }

        var baseVersion = _repository.TryLoad(gameDirectory, baseVersionId)
                          ?? throw new InvalidOperationException($"基底版本 {baseVersionId} 还没有安装");

        _repository.Save(gameDirectory, new GameVersionJson
        {
            Id = instanceId,
            InheritsFrom = baseVersionId,
            Jar = baseVersionId,
            Type = baseVersion.Type ?? "release",
        });

        var config = Load(gameDirectory, instanceId);
        config.DisplayName = string.IsNullOrWhiteSpace(displayName) ? instanceId : displayName.Trim();
        config.Isolated = isolated;
        Save(gameDirectory, instanceId, config);

        if (isolated)
        {
            EnsureIsolatedLayout(gameDirectory, instanceId);
        }

        _log.Info($"已创建实例 {instanceId}（继承 {baseVersionId}）");
        return instanceId;
    }

    /// <summary>
    /// 把加载器安装生成的版本目录接管为实例：重命名到实例名，并把客户端 jar 指回基底原版，
    /// 避免启动时去找一个不存在的 <c>versions/&lt;实例名&gt;/&lt;实例名&gt;.jar</c>。
    /// </summary>
    public string AdoptLoaderVersion(
        string gameDirectory,
        string technicalVersionId,
        string instanceId,
        string baseVersionId,
        string loaderDescription,
        string? displayName = null,
        bool isolated = true)
    {
        if (!string.Equals(technicalVersionId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            _repository.Rename(gameDirectory, technicalVersionId, instanceId);
        }

        var version = _repository.TryLoad(gameDirectory, instanceId)
                      ?? throw new InvalidOperationException($"加载器版本 {instanceId} 不存在");

        // Forge/Fabric 的版本 JSON 不带 jar 字段，合并后 ResolveJarId 会指向实例自己。
        if (string.IsNullOrWhiteSpace(version.Jar))
        {
            version.Jar = baseVersionId;
            _repository.Save(gameDirectory, version);
        }

        var config = Load(gameDirectory, instanceId);
        config.DisplayName = string.IsNullOrWhiteSpace(displayName) ? instanceId : displayName.Trim();
        config.Isolated = isolated;
        if (!config.Loaders.Contains(loaderDescription, StringComparer.OrdinalIgnoreCase))
        {
            config.Loaders.Add(loaderDescription);
        }

        Save(gameDirectory, instanceId, config);

        if (isolated)
        {
            EnsureIsolatedLayout(gameDirectory, instanceId);
        }

        _log.Info($"已把 {technicalVersionId} 接管为实例 {instanceId}（{loaderDescription}）");
        return instanceId;
    }

    /// <summary>
    /// 卸载实例上的加载器：把版本 JSON 还原成只继承原版基底的薄配置，
    /// mods / 存档 / 配置保持不变（加载器的库文件留在共享 libraries 里，不影响原版启动）。
    /// 只对「基底版本已安装且不是实例自身」的实例生效。
    /// </summary>
    public bool RemoveLoader(string gameDirectory, string versionId, string? baseVersionId)
    {
        if (string.IsNullOrWhiteSpace(baseVersionId)
            || string.Equals(baseVersionId, versionId, StringComparison.OrdinalIgnoreCase)
            || !_repository.IsInstalled(gameDirectory, baseVersionId))
        {
            return false;
        }

        var version = _repository.TryLoad(gameDirectory, versionId);
        if (version is null)
        {
            return false;
        }

        _repository.Save(gameDirectory, new GameVersionJson
        {
            Id = versionId,
            InheritsFrom = baseVersionId,
            Jar = baseVersionId,
            Type = version.Type ?? "release",
        });

        var config = Load(gameDirectory, versionId);
        config.Loaders.Clear();
        Save(gameDirectory, versionId, config);

        _log.Info($"已卸载实例 {versionId} 的加载器，恢复为继承 {baseVersionId}");
        return true;
    }
}
