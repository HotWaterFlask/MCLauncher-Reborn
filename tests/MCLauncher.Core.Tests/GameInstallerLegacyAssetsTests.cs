using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>
/// virtual / map_to_resources 展开：这两个标记在**索引文件**里，
/// 每个"原始资源名"要在 virtual 目录下再铺一份，并把 assets_root 指过去。
/// 漏掉的话远古版本贴图全丢，所以单独锁死行为。
/// </summary>
public sealed class GameInstallerLegacyAssetsTests
{
    private const string IconHash = "bdf48ef6b5d0d23bbb02e17d04865216179f510a";
    private const string SoundHash = "abcdef0123456789abcdef0123456789abcdef01";

    private static GameInstaller Create(out HttpDownloader downloader)
    {
        var log = new TestLog();
        downloader = new HttpDownloader(log, new FakeHttpHandler("x"));
        var resolver = new DownloadSourceResolver(log);
        return new GameInstaller(
            downloader,
            resolver,
            new DownloadBatch(downloader, log),
            new VersionRepository(new VersionResolver(log), log),
            log);
    }

    private static AssetIndex BuildIndex(bool isVirtual, bool mapToResources)
    {
        var index = new AssetIndex { Virtual = isVirtual, MapToResources = mapToResources };
        index.Objects["icons/icon_16x16.png"] = new AssetObject { Hash = IconHash, Size = 3 };
        index.Objects["minecraft/sounds/click.ogg"] = new AssetObject { Hash = SoundHash, Size = 4 };
        return index;
    }

    private static void SeedObjects(string gameDirectory)
    {
        WriteObject(gameDirectory, IconHash, "png");
        WriteObject(gameDirectory, SoundHash, "ogg!");
    }

    private static void WriteObject(string gameDirectory, string hash, string content)
    {
        var path = GameInstaller.AssetsObjectPath(gameDirectory, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void virtual索引展开到虚拟目录()
    {
        using var temp = new TempDirectory();
        var installer = Create(out var downloader);
        using (downloader)
        {
            SeedObjects(temp.Path);
            var index = BuildIndex(isVirtual: true, mapToResources: false);
            var info = new AssetIndexInfo { Id = "legacy", Url = "https://example.invalid/index.json" };

            var copied = installer.MaterializeLegacyAssets(temp.Path, info, index);

            Assert.Equal(2, copied);
            var virtualRoot = GameInstaller.VirtualAssetsDirectory(temp.Path, "legacy");
            Assert.True(File.Exists(Path.Combine(virtualRoot, "icons", "icon_16x16.png")));
            Assert.True(File.Exists(Path.Combine(virtualRoot, "minecraft", "sounds", "click.ogg")));

            // virtual 索引的 assets_root 必须指向虚拟目录，否则贴图丢失。
            Assert.Equal(virtualRoot, GameInstaller.ResolveAssetsRoot(temp.Path, info, index));
        }
    }

    [Fact]
    public void mapToResources索引同时铺到resources目录()
    {
        using var temp = new TempDirectory();
        var installer = Create(out var downloader);
        using (downloader)
        {
            SeedObjects(temp.Path);
            var index = BuildIndex(isVirtual: false, mapToResources: true);
            var info = new AssetIndexInfo { Id = "pre-1.6", Url = "https://example.invalid/index.json" };

            installer.MaterializeLegacyAssets(temp.Path, info, index);

            Assert.True(File.Exists(Path.Combine(GameInstaller.ResourcesDirectory(temp.Path), "icons", "icon_16x16.png")));
            Assert.True(File.Exists(Path.Combine(
                GameInstaller.VirtualAssetsDirectory(temp.Path, "pre-1.6"), "minecraft", "sounds", "click.ogg")));
        }
    }

    [Fact]
    public void 普通索引不展开且assets_root保持在assets根()
    {
        using var temp = new TempDirectory();
        var installer = Create(out var downloader);
        using (downloader)
        {
            SeedObjects(temp.Path);
            var index = BuildIndex(isVirtual: false, mapToResources: false);
            var info = new AssetIndexInfo { Id = "12", Url = "https://example.invalid/index.json" };

            Assert.Equal(0, installer.MaterializeLegacyAssets(temp.Path, info, index));
            Assert.False(Directory.Exists(GameInstaller.VirtualAssetsDirectory(temp.Path, "12")));
            Assert.Equal(GameInstaller.AssetsDirectory(temp.Path), GameInstaller.ResolveAssetsRoot(temp.Path, info, index));
        }
    }

    [Fact]
    public void 对象文件缺失时跳过不会崩()
    {
        using var temp = new TempDirectory();
        var installer = Create(out var downloader);
        using (downloader)
        {
            // 故意不铺 objects
            var index = BuildIndex(isVirtual: true, mapToResources: false);
            var info = new AssetIndexInfo { Id = "legacy", Url = "https://example.invalid/index.json" };

            Assert.Equal(0, installer.MaterializeLegacyAssets(temp.Path, info, index));
            // 一个都没展开成功 → assets_root 保持不动，避免把游戏指到空目录。
            Assert.Equal(GameInstaller.AssetsDirectory(temp.Path), GameInstaller.ResolveAssetsRoot(temp.Path, info, index));
        }
    }

    [Fact]
    public void 重复展开不重复复制()
    {
        using var temp = new TempDirectory();
        var installer = Create(out var downloader);
        using (downloader)
        {
            SeedObjects(temp.Path);
            var index = BuildIndex(isVirtual: true, mapToResources: false);
            var info = new AssetIndexInfo { Id = "legacy", Url = "https://example.invalid/index.json" };

            Assert.Equal(2, installer.MaterializeLegacyAssets(temp.Path, info, index));
            Assert.Equal(0, installer.MaterializeLegacyAssets(temp.Path, info, index));
        }
    }

    [Fact]
    public void launcher_profiles路径符合约定()
    {
        using var temp = new TempDirectory();
        var installer = Create(out var downloader);
        using (downloader)
        {
            installer.CreateLauncherProfiles(temp.Path);

            Assert.True(File.Exists(Path.Combine(temp.Path, "launcher_profiles.json")));
            Assert.True(File.Exists(Path.Combine(temp.Path, "launcher_profiles_microsoft_store.json")));

            var json = File.ReadAllText(Path.Combine(temp.Path, "launcher_profiles.json"));
            Assert.Contains("\"profiles\"", json);

            // 已存在时不覆盖
            File.WriteAllText(Path.Combine(temp.Path, "launcher_profiles.json"), "{\"profiles\":{\"mine\":{}}}");
            installer.CreateLauncherProfiles(temp.Path);
            Assert.Contains("mine", File.ReadAllText(Path.Combine(temp.Path, "launcher_profiles.json")));
        }
    }

    [Fact]
    public void 清理受管目录会删除旧清单与空目录()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, ".mclauncher");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");

        GameInstaller.CleanupManagedDirectory(temp.Path);

        Assert.False(Directory.Exists(directory));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "mods")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "schematics")));
    }
}
