using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>
/// 整合包的**类型识别顺序**是这个模块最容易写错的地方：
/// MCBBS 的包里常常同时带 manifest.json，顺序错了就会把它当成 CurseForge。
/// 这里用合成压缩包逐条锁死。
/// </summary>
public sealed class ModpackImporterTests
{
    private static ModpackImporter Create(out TestLog log, out HttpDownloader downloader)
    {
        log = new TestLog();
        downloader = new HttpDownloader(log, new FakeHttpHandler("{}"));
        return new ModpackImporter(downloader, new DownloadSourceResolver(log), new ResourceRepository(log), log);
    }

    [Fact]
    public void 识别Modrinth整合包()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.mrpack");
            ZipBuilder.Create(archive,
                ("modrinth.index.json", """
                {"name":"性能整合","versionId":"1.0","dependencies":{"minecraft":"1.20.4","fabric-loader":"0.15.3"},
                 "files":[{"path":"mods/sodium.jar","hashes":{"sha1":"aa"},"downloads":["https://cdn.example.com/sodium.jar"],"fileSize":100}]}
                """),
                ("overrides/config/foo.toml", "x=1"));

            var info = importer.Inspect(archive);

            Assert.Equal(ModpackKind.Modrinth, info.Kind);
            Assert.Equal("性能整合", info.Name);
            Assert.Equal("1.20.4", info.GameVersion);
            Assert.Equal("Fabric 0.15.3", info.Loader);
            Assert.Equal(1, info.FileCount);
        }
    }

    [Fact]
    public void 识别MCBBS整合包()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.zip");
            ZipBuilder.Create(archive,
                ("mcbbs.packmeta", """
                {"name":"MCBBS包","version":"2.0",
                 "addons":[{"files":[{"path":"mods/a.jar","url":"https://example.com/a.jar","size":10},
                                     {"path":"mods/b.jar","size":20}]}],
                 "launchInfo":{"version":"1.12.2","loaderType":"Forge","loaderVersion":"14.23.5.2860"}}
                """),
                ("manifest.json", """{"addons":[]}"""));

            var info = importer.Inspect(archive);

            Assert.Equal(ModpackKind.Mcbbs, info.Kind);
            Assert.Equal("MCBBS包", info.Name);
            Assert.Equal("1.12.2", info.GameVersion);
            Assert.Equal("Forge 14.23.5.2860", info.Loader);
            Assert.Equal(2, info.FileCount);
        }
    }

    [Fact]
    public void 带addons的manifest也识别为MCBBS()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.zip");
            ZipBuilder.Create(archive, ("manifest.json", """{"name":"x","addons":[{"files":[]}]}"""));

            Assert.Equal(ModpackKind.Mcbbs, importer.Inspect(archive).Kind);
        }
    }

    [Fact]
    public void 识别MultiMC整合包()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.zip");
            ZipBuilder.Create(archive,
                ("mmc-pack.json", """
                {"formatVersion":1,"components":[
                  {"uid":"net.minecraft","version":"1.20.4","cachedName":"Minecraft"},
                  {"uid":"net.neoforged","version":"20.4.237","cachedName":"NeoForge"}]}
                """),
                ("minecraft/config/x.toml", "y=2"));

            var info = importer.Inspect(archive);

            Assert.Equal(ModpackKind.Mmc, info.Kind);
            Assert.Equal("1.20.4", info.GameVersion);
            Assert.Contains("NeoForge", info.Loader);
        }
    }

    [Fact]
    public void 识别CurseForge整合包并提示需要APIKey()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.zip");
            ZipBuilder.Create(archive, ("manifest.json", """
            {"name":"CF包","version":"1.0","overrides":"overrides",
             "minecraft":{"version":"1.19.2","modLoaders":[{"id":"forge-43.2.0","primary":true}]},
             "files":[{"projectID":238222,"fileID":123456,"required":true}]}
            """));

            var info = importer.Inspect(archive);

            Assert.Equal(ModpackKind.CurseForge, info.Kind);
            Assert.Equal("1.19.2", info.GameVersion);
            Assert.Equal("forge-43.2.0", info.Loader);
            Assert.Equal(1, info.FileCount);
            Assert.Contains("API Key", info.Note);
        }
    }

    [Fact]
    public void 识别普通压缩包()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("plain.zip");
            ZipBuilder.Create(archive, ("wrapper/.minecraft/config/a.toml", "z=3"), ("wrapper/.minecraft/mods/b.jar", "jar"));

            Assert.Equal(ModpackKind.PlainArchive, importer.Inspect(archive).Kind);
        }
    }

    [Fact]
    public void rar会被明确拒绝()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.rar");
            File.WriteAllText(archive, "not a rar really");

            var info = importer.Inspect(archive);

            Assert.Equal(ModpackKind.Rar, info.Kind);
            Assert.Contains("rar", info.Note, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task 普通压缩包导入会铺开文件()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("plain.zip");
            ZipBuilder.Create(archive, ("mods/a.jar", "jar-a"), ("config/b.toml", "k=v"));

            var instance = temp.File("instance");
            var result = await importer.ImportAsync(new ModpackImportRequest
            {
                ArchivePath = archive,
                GameRoot = temp.Path,
                InstanceDirectory = instance,
                DownloadFiles = false,
            });

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(instance, "mods", "a.jar")));
            Assert.True(File.Exists(Path.Combine(instance, "config", "b.toml")));
        }
    }

    [Fact]
    public async Task 没有APIKey时CurseForge导入给出可操作提示()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.zip");
            ZipBuilder.Create(archive, ("manifest.json", """{"name":"x","files":[{"projectID":1,"fileID":2}]}"""));

            var result = await importer.ImportAsync(new ModpackImportRequest
            {
                ArchivePath = archive,
                GameRoot = temp.Path,
                InstanceDirectory = temp.File("instance"),
            });

            Assert.False(result.Success);
            Assert.Contains("API Key", result.Error);
        }
    }

    [Fact]
    public async Task Modrinth整合包会铺开overrides()
    {
        using var temp = new TempDirectory();
        var importer = Create(out _, out var downloader);
        using (downloader)
        {
            var archive = temp.File("pack.mrpack");
            ZipBuilder.Create(archive,
                ("modrinth.index.json", """{"name":"n","dependencies":{"minecraft":"1.20.4"},"files":[]}"""),
                ("overrides/config/client.toml", "a=1"),
                ("client-overrides/options.txt", "fov:0.5"));

            var instance = temp.File("instance");
            var result = await importer.ImportAsync(new ModpackImportRequest
            {
                ArchivePath = archive,
                GameRoot = temp.Path,
                InstanceDirectory = instance,
                DownloadFiles = false,
            });

            Assert.True(result.Success);
            Assert.Equal(2, result.Overwritten);
            Assert.True(File.Exists(Path.Combine(instance, "config", "client.toml")));
            Assert.True(File.Exists(Path.Combine(instance, "options.txt")));
        }
    }
}
