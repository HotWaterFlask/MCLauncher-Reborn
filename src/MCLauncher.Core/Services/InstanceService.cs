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

    /// <summary>「为安装加载器临时下载的原版基底」标记文件名。</summary>
    private const string AutoBaseMarkerFileName = "auto-base";

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

    /// <summary>临时基底标记路径。</summary>
    private static string AutoBaseMarkerPath(string gameDirectory, string versionId) =>
        Path.Combine(VersionRepository.VersionDirectory(gameDirectory, versionId), ".mclauncher", AutoBaseMarkerFileName);

    /// <summary>
    /// 标记某个原版版本是「为安装加载器临时下载的基底」。
    /// 它既不在实例列表里展示，也会在加载器合并完成后被清理；
    /// 不做标记的话，首次安装失败后重试时会被误判成用户自己的原版实例而一直保留。
    /// </summary>
    public void MarkAsAutoBase(string gameDirectory, string versionId)
    {
        var path = AutoBaseMarkerPath(gameDirectory, versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DateTimeOffset.Now.ToString("O"));
        _log.Trace($"标记临时原版基底：{versionId}");
    }

    /// <summary>该版本是否是临时基底（没有用户显式建实例）。</summary>
    private static bool IsAutoBase(string gameDirectory, string versionId) =>
        File.Exists(AutoBaseMarkerPath(gameDirectory, versionId));

    /// <summary>
    /// 把已安装的原版版本登记为显式实例（写 instance.json），并去掉临时基底标记。
    /// 用户选择「直接安装原版」时调用，这样以后再加加载器也不会把这份原版当临时基底删掉。
    /// </summary>
    public void RegisterVersionAsInstance(string gameDirectory, string versionId, string? displayName = null)
    {
        var config = Load(gameDirectory, versionId);
        config.DisplayName = string.IsNullOrWhiteSpace(displayName) ? versionId : displayName.Trim();
        // 纯原版实例的版本 id 就是游戏版本号；记下来，改名后仍能按原版版本筛选资源。
        config.GameVersion ??= versionId;
        Save(gameDirectory, versionId, config);

        var marker = AutoBaseMarkerPath(gameDirectory, versionId);
        if (File.Exists(marker))
        {
            File.Delete(marker);
        }
    }

    /// <summary>
    /// 为一个已经由安装器直接写好的自洽实例补上配置：
    /// 记录原版版本与加载器，并保存原版 JSON 快照供「卸载加载器」还原。
    /// </summary>
    public void RegisterSelfContainedInstance(
        string gameDirectory,
        string instanceId,
        string baseVersionId,
        GameVersionJson vanillaVersion,
        string? loaderDescription = null,
        string? displayName = null,
        bool isolated = true)
    {
        vanillaVersion.Id = baseVersionId;
        vanillaVersion.InheritsFrom = null;
        JsonUtil.SerializeToFile(VanillaVersionPath(gameDirectory, instanceId), vanillaVersion);

        var config = Load(gameDirectory, instanceId);
        config.DisplayName = string.IsNullOrWhiteSpace(displayName) ? instanceId : displayName.Trim();
        config.Isolated = isolated;
        config.GameVersion = baseVersionId;

        if (!string.IsNullOrWhiteSpace(loaderDescription) &&
            !config.Loaders.Contains(loaderDescription, StringComparer.OrdinalIgnoreCase))
        {
            config.Loaders.Add(loaderDescription);
        }

        Save(gameDirectory, instanceId, config);

        _log.Info($"已登记自洽实例 {instanceId}（原版 {baseVersionId}）");
    }

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

    /// <summary>读取实例目录里保存的原版版本号（自洽实例的还原快照）；没有快照时返回 null。</summary>
    public string? ReadVanillaVersionId(string gameDirectory, string versionId)
    {
        var path = VanillaVersionPath(gameDirectory, versionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var vanilla = JsonUtil.DeserializeFromFile<GameVersionJson>(path);
            return string.IsNullOrWhiteSpace(vanilla?.Id) ? null : vanilla!.Id!.Trim();
        }
        catch (Exception ex)
        {
            _log.Warn($"原版快照解析失败，已忽略：{path}", ex);
            return null;
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

    /// <summary>
    /// 列出界面上应当展示为实例的版本 id。
    /// 只被其他版本继承、自己没有实例配置的原版基底不算独立实例：
    /// 例如「26.3 + Fabric」只会显示 Fabric 实例，而不会再多出一条 26.3。
    /// </summary>
    public IReadOnlyList<string> ListVisibleInstanceIds(string gameDirectory)
    {
        var ids = _repository.ListInstalledIds(gameDirectory);
        if (ids.Count == 0)
        {
            return ids;
        }

        var inherited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var version = _repository.TryLoad(gameDirectory, id);
            if (!string.IsNullOrWhiteSpace(version?.InheritsFrom))
            {
                inherited.Add(version.InheritsFrom);
            }
        }

        var result = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            var hasConfig = File.Exists(ConfigPath(gameDirectory, id));

            // 只在安装加载器期间临时下载的原版基底，不算实例。
            if (!hasConfig && IsAutoBase(gameDirectory, id))
            {
                continue;
            }

            // 有 instance.json 说明它被显式当成实例（如单独创建的原版实例），继续展示。
            if (inherited.Contains(id) && !hasConfig)
            {
                continue;
            }

            result.Add(id);
        }

        return result;
    }

    // ------------------------------------------------------------------ 创建实例

    /// <summary>
    /// 把用户输入的实例名规整成合法且不冲突的目录名：替换非法字符，冲突时追加 (2)、(3)。
    /// </summary>
    public string CreateUniqueInstanceId(string gameDirectory, string? desiredName, string fallbackName)
    {
        var name = SanitizeInstanceName(desiredName, fallbackName);

        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(VersionRepository.VersionDirectory(gameDirectory, candidate)))
        {
            candidate = $"{name} ({suffix++})";
        }

        return candidate;
    }

    /// <summary>
    /// 重命名实例：同步重命名 versions 下的目录、版本 JSON 与客户端 jar，
    /// 并修正自洽实例的 jar 字段。返回最终使用的实例 id（非法字符会被替换、重名会追加序号）。
    /// </summary>
    public string RenameInstance(string gameDirectory, string instanceId, string desiredName)
    {
        if (string.IsNullOrWhiteSpace(desiredName))
        {
            return instanceId;
        }

        var name = SanitizeInstanceName(desiredName, instanceId);
        if (string.Equals(name, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return instanceId;
        }

        var newId = name;
        var suffix = 2;
        while (Directory.Exists(VersionRepository.VersionDirectory(gameDirectory, newId)))
        {
            newId = $"{name} ({suffix++})";
        }

        _repository.Rename(gameDirectory, instanceId, newId);

        var version = _repository.TryLoad(gameDirectory, newId)
                      ?? throw new InvalidOperationException($"重命名后的版本 {newId} 不存在");

        // 自洽实例的 jar 字段指向旧 id，需要跟着改名。
        if (string.Equals(version.Jar, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            version.Jar = newId;
            _repository.Save(gameDirectory, version);
        }

        var config = Load(gameDirectory, newId);
        config.DisplayName = newId;
        Save(gameDirectory, newId, config);

        _log.Info($"实例已重命名：{instanceId} → {newId}");
        return newId;
    }

    private static string SanitizeInstanceName(string? desiredName, string fallbackName)
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

        return name;
    }

    /// <summary>
    /// 创建一个独立实例：把基底原版的版本 JSON 合并进来、客户端 jar 复制到实例目录，
    /// 实例文件夹因此是自洽的（与 HMCL 一样，versions 下只保留一个目录）。
    /// </summary>
    public string CreateProfileInstance(
        string gameDirectory,
        string instanceId,
        string baseVersionId,
        string? displayName = null,
        bool isolated = true,
        bool deleteBaseVersion = false)
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
        config.GameVersion = baseVersionId;
        Save(gameDirectory, instanceId, config);

        FlattenInstance(gameDirectory, instanceId, baseVersionId, deleteBaseVersion);
        _log.Info($"已创建实例 {instanceId}（原版 {baseVersionId}）");
        return instanceId;
    }

    /// <summary>
    /// 把加载器安装生成的版本目录接管为实例：重命名到实例名，合并继承链成自洽 JSON，
    /// jar 指向实例自身；需要时删除临时基底目录，versions 下只保留一个实例文件夹。
    /// </summary>
    public string AdoptLoaderVersion(
        string gameDirectory,
        string technicalVersionId,
        string instanceId,
        string baseVersionId,
        string loaderDescription,
        string? displayName = null,
        bool isolated = true,
        bool deleteBaseVersion = false)
    {
        if (!string.Equals(technicalVersionId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            _repository.Rename(gameDirectory, technicalVersionId, instanceId);
        }

        var version = _repository.TryLoad(gameDirectory, instanceId)
                      ?? throw new InvalidOperationException($"加载器版本 {instanceId} 不存在");

        var config = Load(gameDirectory, instanceId);
        config.DisplayName = string.IsNullOrWhiteSpace(displayName) ? instanceId : displayName.Trim();
        config.Isolated = isolated;
        config.GameVersion = baseVersionId;
        if (!config.Loaders.Contains(loaderDescription, StringComparer.OrdinalIgnoreCase))
        {
            config.Loaders.Add(loaderDescription);
        }

        Save(gameDirectory, instanceId, config);

        FlattenInstance(gameDirectory, instanceId, baseVersionId, deleteBaseVersion);
        _log.Info($"已把 {technicalVersionId} 接管为实例 {instanceId}（{loaderDescription}）");
        return instanceId;
    }

    /// <summary>实例目录内保存的原版版本 JSON（用于卸载加载器后还原）。</summary>
    private static string VanillaVersionPath(string gameDirectory, string versionId) =>
        Path.Combine(VersionRepository.VersionDirectory(gameDirectory, versionId), ".mclauncher", "vanilla.json");

    /// <summary>
    /// 把继承链合并进实例 JSON，使其不再依赖基底目录；缺少实例 jar 时从基底复制。
    /// </summary>
    private void FlattenInstance(
        string gameDirectory,
        string instanceId,
        string baseVersionId,
        bool deleteBaseVersion)
    {
        var sameVersion = string.Equals(baseVersionId, instanceId, StringComparison.OrdinalIgnoreCase);

        if (!sameVersion)
        {
            // 卸载加载器时要回到原版，先把原版 JSON 留一份在实例目录里。
            var vanilla = _repository.Resolve(gameDirectory, baseVersionId);
            vanilla.Id = baseVersionId;
            JsonUtil.SerializeToFile(VanillaVersionPath(gameDirectory, instanceId), vanilla);
        }

        var merged = _repository.Resolve(gameDirectory, instanceId);
        merged.Id = instanceId;
        merged.InheritsFrom = null;
        merged.Jar = instanceId;
        _repository.Save(gameDirectory, merged);

        var instanceJar = VersionRepository.VersionJarPath(gameDirectory, instanceId);
        if (!File.Exists(instanceJar) && !sameVersion)
        {
            var baseJar = VersionRepository.VersionJarPath(gameDirectory, baseVersionId);
            if (File.Exists(baseJar))
            {
                File.Copy(baseJar, instanceJar, overwrite: true);
            }
        }

        if (deleteBaseVersion)
        {
            TryDeleteUnusedBase(gameDirectory, baseVersionId, instanceId, installedNow: true);
        }
        else if (IsAutoBase(gameDirectory, baseVersionId))
        {
            // 上一次安装加载器失败了，只留下一个临时基底；这次合并成功后一并清理。
            TryDeleteUnusedBase(gameDirectory, baseVersionId, instanceId, installedNow: false);
        }
    }

    /// <summary>基底只是临时基底、没有其他版本继承时，删掉多余的原版目录。</summary>
    private void TryDeleteUnusedBase(
        string gameDirectory,
        string baseVersionId,
        string instanceId,
        bool installedNow)
    {
        if (string.Equals(baseVersionId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 用户显式创建的原版实例（有 instance.json）必须保留。
        if (File.Exists(ConfigPath(gameDirectory, baseVersionId)))
        {
            return;
        }

        // 既不是本次安装的基底、也没有临时基底标记：可能是用户或官方启动器原有的版本，保留。
        if (!installedNow && !IsAutoBase(gameDirectory, baseVersionId))
        {
            return;
        }

        foreach (var id in _repository.ListInstalledIds(gameDirectory))
        {
            if (string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = _repository.TryLoad(gameDirectory, id);
            if (string.Equals(version?.InheritsFrom, baseVersionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        if (_repository.Delete(gameDirectory, baseVersionId))
        {
            _log.Info($"基底 {baseVersionId} 已合并进实例 {instanceId}，删除多余的原版目录");
        }
    }

    /// <summary>
    /// 卸载实例上的加载器：基底还在时还原为继承原版；基底已被合并删除时，
    /// 用实例目录里保存的原版 JSON 还原成自洽原版实例。mods / 存档 / 配置保持不变。
    /// </summary>
    public bool RemoveLoader(string gameDirectory, string versionId, string? baseVersionId)
    {
        if (string.IsNullOrWhiteSpace(baseVersionId)
            || string.Equals(baseVersionId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var version = _repository.TryLoad(gameDirectory, versionId);
        if (version is null)
        {
            return false;
        }

        var baseVersion = _repository.TryLoad(gameDirectory, baseVersionId);
        if (baseVersion is not null)
        {
            _repository.Save(gameDirectory, new GameVersionJson
            {
                Id = versionId,
                InheritsFrom = baseVersionId,
                Jar = baseVersionId,
                Type = version.Type ?? "release",
            });
        }
        else
        {
            var vanillaPath = VanillaVersionPath(gameDirectory, versionId);
            var vanilla = File.Exists(vanillaPath)
                ? JsonUtil.DeserializeFromFile<GameVersionJson>(vanillaPath)
                : null;

            if (vanilla is null)
            {
                return false;
            }

            vanilla.Id = versionId;
            vanilla.InheritsFrom = null;
            vanilla.Jar = versionId;
            _repository.Save(gameDirectory, vanilla);
        }

        var config = Load(gameDirectory, versionId);
        config.Loaders.Clear();
        Save(gameDirectory, versionId, config);

        _log.Info($"已卸载实例 {versionId} 的加载器，恢复为原版 {baseVersionId}");
        return true;
    }
}
