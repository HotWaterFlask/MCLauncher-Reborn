using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>自更新换位：旧文件改名成 .old，新文件顶上，整个过程可回滚。</summary>
public sealed class LauncherUpdaterTests
{
    private static LauncherUpdater Create(TempDirectory temp, TestLog log, out HttpDownloader downloader)
    {
        downloader = new HttpDownloader(log, new FakeHttpHandler("unused"));
        return new LauncherUpdater(downloader, log, temp.Path);
    }

    [Fact]
    public void 替换后新文件就位且旧文件保留为备份()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        var updater = Create(temp, log, out var downloader);
        using (downloader)
        {
            var exe = temp.File("MCLauncher-Reborn.exe");
            File.WriteAllText(exe, "old");

            Directory.CreateDirectory(Path.GetDirectoryName(updater.StagedFile)!);
            File.WriteAllText(updater.StagedFile, "new");

            updater.Apply(exe);

            Assert.Equal("new", File.ReadAllText(exe));
            Assert.True(File.Exists(exe + ".old"));
            Assert.Equal("old", File.ReadAllText(exe + ".old"));
            Assert.False(File.Exists(updater.StagedFile));
        }
    }

    [Fact]
    public void 暂存文件不存在时不改动原文件()
    {
        using var temp = new TempDirectory();
        var log = new TestLog();
        var updater = Create(temp, log, out var downloader);
        using (downloader)
        {
            var exe = temp.File("MCLauncher-Reborn.exe");
            File.WriteAllText(exe, "old");

            Assert.Throws<FileNotFoundException>(() => updater.Apply(exe));
            Assert.Equal("old", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".old"));
        }
    }
}
