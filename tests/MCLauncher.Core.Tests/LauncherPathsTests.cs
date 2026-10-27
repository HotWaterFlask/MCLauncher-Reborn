using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class LauncherPathsTests
{
    [Fact]
    public void 旧配置目录会改名成新目录()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, LauncherPaths.LegacyConfigFolderName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
        var log = new TestLog();

        var directory = LauncherPaths.EnsureConfigDirectory(temp.Path, log);

        Assert.Equal(Path.Combine(temp.Path, LauncherPaths.ConfigFolderName), directory);
        Assert.False(Directory.Exists(legacy));
        Assert.True(File.Exists(Path.Combine(directory, "settings.json")));
        Assert.True(log.Contains("配置目录已迁移"));
    }

    [Fact]
    public void 新目录已存在时不动旧目录()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, LauncherPaths.LegacyConfigFolderName);
        var current = Path.Combine(temp.Path, LauncherPaths.ConfigFolderName);
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

        var directory = LauncherPaths.EnsureConfigDirectory(temp.Path);

        Assert.Equal(current, directory);
        Assert.True(File.Exists(Path.Combine(legacy, "settings.json")));
    }

    [Fact]
    public void 没有旧目录时直接使用新目录()
    {
        using var temp = new TempDirectory();

        var directory = LauncherPaths.EnsureConfigDirectory(temp.Path);

        Assert.Equal(Path.Combine(temp.Path, LauncherPaths.ConfigFolderName), directory);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void 旧目录被占用时回退到旧目录()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, LauncherPaths.LegacyConfigFolderName);
        Directory.CreateDirectory(legacy);
        var settings = Path.Combine(legacy, "settings.json");
        File.WriteAllText(settings, "{}");
        var log = new TestLog();

        string directory;
        // 模拟另一个实例仍占用旧目录里的日志文件，Windows 下无法重命名该目录。
        using (File.Open(settings, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            directory = LauncherPaths.EnsureConfigDirectory(temp.Path, log);
        }

        Assert.Equal(legacy, directory);
        Assert.True(File.Exists(settings));
        Assert.True(log.Contains("配置目录迁移失败"));

        // 占用解除后再次调用即可完成迁移。
        Assert.Equal(
            Path.Combine(temp.Path, LauncherPaths.ConfigFolderName),
            LauncherPaths.EnsureConfigDirectory(temp.Path));
    }
}
