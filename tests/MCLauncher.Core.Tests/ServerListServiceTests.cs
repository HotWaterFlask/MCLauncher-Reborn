using System.Text;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

public sealed class ServerListServiceTests
{
    private static ServerListService Create(out TestLog log)
    {
        log = new TestLog();
        return new ServerListService(log);
    }

    [Fact]
    public void 文件不存在时返回空列表()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);

        Assert.Empty(service.Load(temp.Path));
    }

    [Fact]
    public void 保存后可以完整读回()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);

        var entries = new[]
        {
            new MinecraftServerEntry
            {
                Name = "主服",
                Address = "play.example.com:25565",
                IconBase64 = "iVBORw0KGgo=",
                ResourcePolicy = ServerResourcePackPolicy.Always,
            },
            new MinecraftServerEntry
            {
                Name = "备用",
                Address = "mc.example.net",
                ResourcePolicy = ServerResourcePackPolicy.Never,
            },
            new MinecraftServerEntry
            {
                Name = "默认策略",
                Address = "localhost",
            },
        };

        Assert.True(service.Save(temp.Path, entries));

        var loaded = service.Load(temp.Path);
        Assert.Equal(3, loaded.Count);
        Assert.Equal("主服", loaded[0].Name);
        Assert.Equal("play.example.com:25565", loaded[0].Address);
        Assert.Equal("iVBORw0KGgo=", loaded[0].IconBase64);
        Assert.Equal(ServerResourcePackPolicy.Always, loaded[0].ResourcePolicy);
        Assert.Equal(ServerResourcePackPolicy.Never, loaded[1].ResourcePolicy);
        Assert.Equal(ServerResourcePackPolicy.Ask, loaded[2].ResourcePolicy);
        Assert.Null(loaded[2].IconBase64);
    }

    [Fact]
    public void 保存空列表后文件仍然存在且读出零条()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);

        Assert.True(service.Save(temp.Path, Array.Empty<MinecraftServerEntry>()));
        Assert.True(File.Exists(ServerListService.GetPath(temp.Path)));
        Assert.Empty(service.Load(temp.Path));
    }

    [Fact]
    public void 读取时跳过未知标签并识别资源包策略()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);
        var path = Path.Combine(temp.Path, ServerListService.FileName);

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write((byte)10);
            WriteString(writer, string.Empty);

            writer.Write((byte)9);
            WriteString(writer, "servers");
            writer.Write((byte)10);
            writer.Write(1);

            writer.Write((byte)8);
            WriteString(writer, "name");
            WriteString(writer, "Hypixel");

            writer.Write((byte)3); // 未知字段：TAG_Int
            WriteString(writer, "hidden");
            writer.Write(42);

            writer.Write((byte)8);
            WriteString(writer, "ip");
            WriteString(writer, "mc.hypixel.net");

            writer.Write((byte)1);
            WriteString(writer, "acceptTextures");
            writer.Write((byte)2);

            writer.Write((byte)0); // 服务器 compound 结束

            writer.Write((byte)10); // 根上的未知 compound
            WriteString(writer, "meta");
            writer.Write((byte)0);

            writer.Write((byte)0); // 根结束
        }

        var entry = Assert.Single(service.Load(temp.Path));
        Assert.Equal("Hypixel", entry.Name);
        Assert.Equal("mc.hypixel.net", entry.Address);
        Assert.Equal(ServerResourcePackPolicy.Never, entry.ResourcePolicy);
        Assert.Null(entry.IconBase64);
    }

    [Fact]
    public void 损坏的文件不会抛异常()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);

        File.WriteAllBytes(ServerListService.GetPath(temp.Path), new byte[] { 0xFF, 0x01, 0x02 });

        Assert.Empty(service.Load(temp.Path));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
}
