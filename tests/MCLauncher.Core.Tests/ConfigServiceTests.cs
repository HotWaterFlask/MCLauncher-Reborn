using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>设置服务：启动时自动把游戏目录建出来。</summary>
public class ConfigServiceTests
{
    private static ConfigService CreateService(string root, out TestLog log)
    {
        log = new TestLog();
        var config = new ConfigService(
            new JsonConfigStore(Path.Combine(root, "config"), log),
            log,
            includeBuiltInSecrets: false);
        config.Load();
        return config;
    }

    private static void Cleanup(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void 首次运行会创建默认的minecraft目录()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcl-config-" + Guid.NewGuid().ToString("N"));
        var launcherDirectory = Path.Combine(root, "launcher");
        Directory.CreateDirectory(launcherDirectory);

        try
        {
            var config = CreateService(root, out var log);

            var selected = config.EnsureDefaultGameFolder(launcherDirectory);

            Assert.Equal(Path.Combine(launcherDirectory, ".minecraft"), selected);
            Assert.True(Directory.Exists(Path.Combine(launcherDirectory, ".minecraft")));
            Assert.Contains(log.Lines, line => line.Contains("已创建默认游戏目录"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 选中的自定义目录不存在时也会被创建()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcl-config-" + Guid.NewGuid().ToString("N"));
        var launcherDirectory = Path.Combine(root, "launcher");
        var custom = Path.Combine(root, "custom-game");
        Directory.CreateDirectory(launcherDirectory);

        try
        {
            var config = CreateService(root, out _);
            config.Settings.SelectedGameFolder = custom;

            var selected = config.EnsureDefaultGameFolder(launcherDirectory);

            Assert.Equal(custom, selected);
            Assert.True(Directory.Exists(custom));
            Assert.True(Directory.Exists(Path.Combine(launcherDirectory, ".minecraft")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 首次加载会标记为首次运行保存后不再是首次()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcl-config-" + Guid.NewGuid().ToString("N"));

        try
        {
            var first = CreateService(root, out _);
            Assert.True(first.IsFirstRun);

            first.Save();

            var second = CreateService(root, out _);
            Assert.False(second.IsFirstRun);
        }
        finally
        {
            Cleanup(root);
        }
    }
}
