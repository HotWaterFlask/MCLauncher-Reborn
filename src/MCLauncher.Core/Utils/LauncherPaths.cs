using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Utils;

/// <summary>启动器便携目录的位置，以及旧版本目录的迁移。</summary>
public static class LauncherPaths
{
    /// <summary>便携配置目录名（位于启动器所在目录）。</summary>
    public const string ConfigFolderName = "mclcfg";

    /// <summary>旧版便携配置目录名，只用于迁移。</summary>
    public const string LegacyConfigFolderName = ".fmmcl";

    /// <summary>
    /// 返回配置目录。只存在旧版目录时先整体改名到新目录；
    /// 改名失败（例如日志文件正被另一个实例占用）就继续用旧目录，避免丢配置。
    /// </summary>
    public static string EnsureConfigDirectory(string baseDirectory, ILogger? log = null)
    {
        var current = Path.Combine(baseDirectory, ConfigFolderName);
        var legacy = Path.Combine(baseDirectory, LegacyConfigFolderName);

        if (Directory.Exists(current) || !Directory.Exists(legacy))
        {
            return current;
        }

        try
        {
            Directory.Move(legacy, current);
            log?.Info($"配置目录已迁移：{LegacyConfigFolderName} → {ConfigFolderName}");
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Warn($"配置目录迁移失败，继续使用旧目录：{legacy}", ex);
            return legacy;
        }
    }
}
