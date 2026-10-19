using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class VersionRepositoryTests
{
    private static VersionRepository Create(out TestLog log)
    {
        log = new TestLog();
        return new VersionRepository(new VersionResolver(log), log);
    }

    [Fact]
    public void 写入后可读回并解析继承链()
    {
        using var temp = new TempDirectory();
        var repository = Create(out _);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.20.4","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"a:b:1"}]}""")!);
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.20.4-forge","inheritsFrom":"1.20.4","libraries":[{"name":"f:g:1"}]}""")!);

        var resolved = repository.Resolve(temp.Path, "1.20.4-forge");

        Assert.Equal("net.minecraft.client.main.Main", resolved.MainClass);
        Assert.Equal(new[] { "f:g:1", "a:b:1" }, resolved.Libraries!.Select(l => l.Name));
    }

    [Fact]
    public void 扫描出本地已安装的版本()
    {
        using var temp = new TempDirectory();
        var repository = Create(out _);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"1.20.4"}""")!);
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"1.19.2"}""")!);

        var ids = repository.ListInstalledIds(temp.Path);
        Assert.Equal(new[] { "1.19.2", "1.20.4" }, ids);
    }

    [Fact]
    public void 未安装的版本解析会报错()
    {
        using var temp = new TempDirectory();
        var repository = Create(out _);

        Assert.False(repository.IsInstalled(temp.Path, "1.20.4"));
        Assert.Throws<InvalidDataException>(() => repository.Resolve(temp.Path, "1.20.4"));
    }

    [Fact]
    public void 版本名不合法时拒绝写入()
    {
        using var temp = new TempDirectory();
        var repository = Create(out _);

        var version = JsonUtil.Deserialize<GameVersionJson>("""{"id":"../escape"}""")!;
        Assert.Throws<ArgumentException>(() => repository.Save(temp.Path, version));
    }

    [Fact]
    public void 重命名会同时改动目录与其中的文件()
    {
        using var temp = new TempDirectory();
        var repository = Create(out _);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"old","mainClass":"X"}""")!);
        File.WriteAllText(VersionRepository.VersionJarPath(temp.Path, "old"), "jar");

        repository.Rename(temp.Path, "old", "new");

        Assert.False(repository.IsInstalled(temp.Path, "old"));
        Assert.True(repository.IsInstalled(temp.Path, "new"));
        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, "new")));
        Assert.Equal("new", repository.TryLoad(temp.Path, "new")!.Id);
    }

    [Fact]
    public void jar字段优先于id作为客户端jar名()
    {
        var version = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"1.12.2-forge","jar":"1.12.2"}""")!;

        Assert.Equal("1.12.2", VersionRepository.ResolveJarId(version));

        var noJar = JsonUtil.Deserialize<GameVersionJson>("""{"id":"1.20.4"}""")!;
        Assert.Equal("1.20.4", VersionRepository.ResolveJarId(noJar));
    }

    [Fact]
    public void 删除版本目录()
    {
        using var temp = new TempDirectory();
        var repository = Create(out _);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"1.20.4"}""")!);

        Assert.True(repository.Delete(temp.Path, "1.20.4"));
        Assert.False(repository.IsInstalled(temp.Path, "1.20.4"));
        Assert.False(repository.Delete(temp.Path, "1.20.4"));
    }
}
