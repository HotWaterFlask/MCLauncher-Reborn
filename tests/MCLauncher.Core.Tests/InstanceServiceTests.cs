using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class InstanceServiceTests
{
    private static InstanceService Create(out VersionRepository repository)
    {
        var log = new TestLog();
        repository = new VersionRepository(new VersionResolver(log), log);
        return new InstanceService(repository, log);
    }

    [Fact]
    public void 加载器实例会隐藏它依赖的原版基底()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"26.3"}""")!);
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"Fabric26.3","inheritsFrom":"26.3"}""")!);
        instances.Save(temp.Path, "Fabric26.3", new InstanceConfig { DisplayName = "Fabric26.3" });

        var visible = instances.ListVisibleInstanceIds(temp.Path);

        Assert.Equal(new[] { "Fabric26.3" }, visible);
    }

    [Fact]
    public void 显式创建的原版实例不会被隐藏()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"26.3"}""")!);
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"Fabric26.3","inheritsFrom":"26.3"}""")!);
        instances.Save(temp.Path, "26.3", new InstanceConfig { DisplayName = "我的原版" });
        instances.Save(temp.Path, "Fabric26.3", new InstanceConfig { DisplayName = "Fabric26.3" });

        var visible = instances.ListVisibleInstanceIds(temp.Path);

        Assert.Equal(new[] { "26.3", "Fabric26.3" }, visible);
    }

    [Fact]
    public void 没有被继承的原版仍然显示()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"1.20.1"}""")!);
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>("""{"id":"26.3"}""")!);
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"Fabric26.3","inheritsFrom":"26.3"}""")!);
        instances.Save(temp.Path, "Fabric26.3", new InstanceConfig());

        var visible = instances.ListVisibleInstanceIds(temp.Path);

        Assert.Equal(new[] { "1.20.1", "Fabric26.3" }, visible);
    }

    [Fact]
    public void 加载器实例会合并成单个自洽目录()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"fabric-loader-0.20.1-26.3","inheritsFrom":"26.3","mainClass":"net.fabricmc.KnotClient","libraries":[{"name":"fabric:b:1"}]}""")!);

        instances.AdoptLoaderVersion(
            temp.Path,
            "fabric-loader-0.20.1-26.3",
            "Fabric26.3",
            "26.3",
            "Fabric 0.20.1",
            "Fabric26.3",
            isolated: true,
            deleteBaseVersion: true);

        Assert.False(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "26.3")));

        var merged = repository.TryLoad(temp.Path, "Fabric26.3")!;
        Assert.Null(merged.InheritsFrom);
        Assert.Equal("Fabric26.3", merged.Jar);
        Assert.Equal("net.fabricmc.KnotClient", merged.MainClass);
        Assert.Equal(new[] { "fabric:b:1", "base:a:1" }, merged.Libraries!.Select(library => library.Name));
        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, "Fabric26.3")));

        var config = instances.Load(temp.Path, "Fabric26.3");
        Assert.Equal("26.3", config.GameVersion);
        Assert.Contains("Fabric 0.20.1", config.Loaders);

        // 实例目录里只保留启动器元数据，游戏目录交给游戏与 Mod 自行创建。
        var instanceDirectory = VersionRepository.VersionDirectory(temp.Path, "Fabric26.3");
        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "mods")));
        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "saves")));
        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "schematics")));
        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "shaderpacks")));

        Assert.Equal(new[] { "Fabric26.3" }, instances.ListVisibleInstanceIds(temp.Path));
    }

    [Fact]
    public void 自洽实例卸载加载器后仍可还原原版()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"fabric-loader-0.20.1-26.3","inheritsFrom":"26.3","mainClass":"net.fabricmc.KnotClient","libraries":[{"name":"fabric:b:1"}]}""")!);

        instances.AdoptLoaderVersion(
            temp.Path, "fabric-loader-0.20.1-26.3", "Fabric26.3", "26.3", "Fabric 0.20.1",
            "Fabric26.3", isolated: true, deleteBaseVersion: true);

        Assert.True(instances.RemoveLoader(temp.Path, "Fabric26.3", "26.3"));

        var vanilla = repository.TryLoad(temp.Path, "Fabric26.3")!;
        Assert.Null(vanilla.InheritsFrom);
        Assert.Equal("Fabric26.3", vanilla.Jar);
        Assert.Equal("net.minecraft.client.main.Main", vanilla.MainClass);
        Assert.Equal(new[] { "base:a:1" }, vanilla.Libraries!.Select(library => library.Name));
        Assert.Empty(instances.Load(temp.Path, "Fabric26.3").Loaders);
        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, "Fabric26.3")));
    }

    [Fact]
    public void 创建原版实例时也会合并成单个目录()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");

        instances.CreateProfileInstance(
            temp.Path, "我的原版", "26.3", "我的原版", isolated: false, deleteBaseVersion: true);

        Assert.False(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "26.3")));

        var instance = repository.TryLoad(temp.Path, "我的原版")!;
        Assert.Null(instance.InheritsFrom);
        Assert.Equal("我的原版", instance.Jar);
        Assert.Equal(new[] { "base:a:1" }, instance.Libraries!.Select(library => library.Name));
        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, "我的原版")));
        Assert.Equal("26.3", instances.Load(temp.Path, "我的原版").GameVersion);
    }

    [Fact]
    public void 首次安装失败留下的临时基底会在重试成功后清理()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");
        // 上一次安装加载器失败，只留下标记过的临时原版基底。
        instances.MarkAsAutoBase(temp.Path, "26.3");
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"fabric-loader-0.20.1-26.3","inheritsFrom":"26.3","mainClass":"net.fabricmc.KnotClient","libraries":[{"name":"fabric:b:1"}]}""")!);

        instances.AdoptLoaderVersion(
            temp.Path, "fabric-loader-0.20.1-26.3", "Fabric26.3", "26.3", "Fabric 0.20.1",
            "Fabric26.3", isolated: true, deleteBaseVersion: false);

        Assert.False(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "26.3")));
        Assert.Equal(new[] { "Fabric26.3" }, instances.ListVisibleInstanceIds(temp.Path));
    }

    [Fact]
    public void 临时基底不会出现在实例列表()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main"}""")!);
        instances.MarkAsAutoBase(temp.Path, "26.3");

        Assert.Empty(instances.ListVisibleInstanceIds(temp.Path));
    }

    [Fact]
    public void 登记成实例后临时基底不会再被清理()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");
        instances.MarkAsAutoBase(temp.Path, "26.3");
        instances.RegisterVersionAsInstance(temp.Path, "26.3");
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"fabric-loader-0.20.1-26.3","inheritsFrom":"26.3","mainClass":"net.fabricmc.KnotClient","libraries":[{"name":"fabric:b:1"}]}""")!);

        instances.AdoptLoaderVersion(
            temp.Path, "fabric-loader-0.20.1-26.3", "Fabric26.3", "26.3", "Fabric 0.20.1",
            "Fabric26.3", isolated: true, deleteBaseVersion: false);

        Assert.True(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "26.3")));
        Assert.Equal(new[] { "26.3", "Fabric26.3" }, instances.ListVisibleInstanceIds(temp.Path));
    }

    [Fact]
    public void 未标记的已有原版基底不会被删除()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"fabric-loader-0.20.1-26.3","inheritsFrom":"26.3","mainClass":"net.fabricmc.KnotClient","libraries":[{"name":"fabric:b:1"}]}""")!);

        instances.AdoptLoaderVersion(
            temp.Path, "fabric-loader-0.20.1-26.3", "Fabric26.3", "26.3", "Fabric 0.20.1",
            "Fabric26.3", isolated: true, deleteBaseVersion: false);

        Assert.True(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "26.3")));
    }

    [Fact]
    public void 直装实例登记后可从原版快照卸载加载器()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        var vanilla = JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!;
        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"Fabric26.3","mainClass":"net.fabricmc.KnotClient","libraries":[{"name":"fabric:b:1"}]}""")!);

        instances.RegisterSelfContainedInstance(
            temp.Path, "Fabric26.3", "26.3", vanilla, "Fabric 0.20.1", "Fabric26.3");

        var config = instances.Load(temp.Path, "Fabric26.3");
        Assert.Equal("26.3", config.GameVersion);
        Assert.Contains("Fabric 0.20.1", config.Loaders);

        // 只登记元数据，不预建 mods 等游戏目录。
        Assert.False(Directory.Exists(Path.Combine(
            VersionRepository.VersionDirectory(temp.Path, "Fabric26.3"), "mods")));

        var vanillaPath = Path.Combine(
            VersionRepository.VersionDirectory(temp.Path, "Fabric26.3"), ".mclauncher", "vanilla.json");
        Assert.True(File.Exists(vanillaPath));
        Assert.Equal("26.3", JsonUtil.DeserializeFromFile<GameVersionJson>(vanillaPath)!.Id);

        Assert.True(instances.RemoveLoader(temp.Path, "Fabric26.3", "26.3"));

        var restored = repository.TryLoad(temp.Path, "Fabric26.3")!;
        Assert.Null(restored.InheritsFrom);
        Assert.Equal("net.minecraft.client.main.Main", restored.MainClass);
        Assert.Equal("Fabric26.3", restored.Jar);
    }

    [Fact]
    public void 重命名实例会同步重命名版本目录与文件()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");

        instances.CreateProfileInstance(
            temp.Path, "OldName", "26.3", "OldName", isolated: false, deleteBaseVersion: true);

        var newId = instances.RenameInstance(temp.Path, "OldName", "Eternal");

        Assert.Equal("Eternal", newId);
        Assert.False(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "OldName")));
        Assert.True(Directory.Exists(VersionRepository.VersionDirectory(temp.Path, "Eternal")));
        Assert.False(repository.IsInstalled(temp.Path, "OldName"));

        var version = repository.TryLoad(temp.Path, "Eternal")!;
        Assert.Equal("Eternal", version.Id);
        Assert.Equal("Eternal", version.Jar);
        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, "Eternal")));
        Assert.False(File.Exists(VersionRepository.VersionJarPath(temp.Path, "OldName")));

        var config = instances.Load(temp.Path, "Eternal");
        Assert.Equal("Eternal", config.DisplayName);
        Assert.Equal("26.3", config.GameVersion);
    }

    [Fact]
    public void 重命名为已存在的实例名会追加序号()
    {
        using var temp = new TempDirectory();
        var instances = Create(out var repository);

        repository.Save(temp.Path, JsonUtil.Deserialize<GameVersionJson>(
            """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"base:a:1"}]}""")!);
        WriteBaseJar(temp, "26.3");

        instances.CreateProfileInstance(
            temp.Path, "One", "26.3", "One", isolated: false, deleteBaseVersion: false);
        instances.CreateProfileInstance(
            temp.Path, "Two", "26.3", "Two", isolated: false, deleteBaseVersion: false);

        var newId = instances.RenameInstance(temp.Path, "One", "Two");

        Assert.Equal("Two (2)", newId);
        Assert.True(repository.IsInstalled(temp.Path, "Two"));
        Assert.True(repository.IsInstalled(temp.Path, "Two (2)"));
        Assert.False(repository.IsInstalled(temp.Path, "One"));
    }

    private static void WriteBaseJar(TempDirectory temp, string versionId)
    {
        var path = VersionRepository.VersionJarPath(temp.Path, versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "jar");
    }
}
