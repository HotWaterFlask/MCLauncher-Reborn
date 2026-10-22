using System.IO.Compression;
using System.Text;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

public sealed class ResourceIconTests
{
    [Fact]
    public void 扫描FabricMod时读出图标与加载器()
    {
        using var temp = new TempDirectory();
        var mods = ResourceRepository.GetDirectory(temp.Path, ResourceKind.Mod);
        Directory.CreateDirectory(mods);

        var icon = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        WriteZip(
            Path.Combine(mods, "testmod.jar"),
            ("fabric.mod.json", """
            {"id":"testmod","name":"Test Mod","version":"1.0.0","icon":"assets/testmod/icon.png"}
            """),
            ("assets/testmod/icon.png", icon));

        var mod = Assert.Single(new ResourceRepository(new TestLog()).Scan(temp.Path, ResourceKind.Mod));

        Assert.Equal("Fabric", mod.Loader);
        Assert.Equal(icon, mod.IconBytes);
    }

    [Fact]
    public void 扫描ForgeMod时读出logoFile()
    {
        using var temp = new TempDirectory();
        var mods = ResourceRepository.GetDirectory(temp.Path, ResourceKind.Mod);
        Directory.CreateDirectory(mods);

        var icon = new byte[] { 1, 2, 3, 4 };
        WriteZip(
            Path.Combine(mods, "forge.jar"),
            ("META-INF/mods.toml", """
            displayName="Forge Mod"
            version="2.0"
            logoFile="logo.png"
            """),
            ("logo.png", icon));

        var mod = Assert.Single(new ResourceRepository(new TestLog()).Scan(temp.Path, ResourceKind.Mod));

        Assert.Equal("Forge", mod.Loader);
        Assert.Equal(icon, mod.IconBytes);
    }

    [Fact]
    public void 扫描资源包时读出pack封面()
    {
        using var temp = new TempDirectory();
        var packs = ResourceRepository.GetDirectory(temp.Path, ResourceKind.ResourcePack);
        Directory.CreateDirectory(packs);

        var icon = new byte[] { 5, 6, 7 };
        WriteZip(
            Path.Combine(packs, "pack.zip"),
            ("pack.mcmeta", """{"pack":{"pack_format":15,"description":"测试"}}"""),
            ("pack.png", icon));

        var pack = Assert.Single(new ResourceRepository(new TestLog()).Scan(temp.Path, ResourceKind.ResourcePack));

        Assert.Equal(icon, pack.IconBytes);
        Assert.Equal("15", pack.Version);
    }

    private static void WriteZip(
        string path,
        (string Name, string Content) textEntry,
        (string Name, byte[] Content) binaryEntry)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var text = zip.CreateEntry(textEntry.Name);
        using (var writer = new StreamWriter(text.Open(), new UTF8Encoding(false)))
        {
            writer.Write(textEntry.Content);
        }

        var binary = zip.CreateEntry(binaryEntry.Name);
        using var stream = binary.Open();
        stream.Write(binaryEntry.Content);
    }
}
