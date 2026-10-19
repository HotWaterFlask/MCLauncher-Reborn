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
}
