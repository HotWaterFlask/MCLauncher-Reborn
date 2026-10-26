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
            WriteInt32(writer, 1);

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
    public void 可以读取游戏原生生成的服务器列表()
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
            WriteInt32(writer, 1);

            writer.Write((byte)1);
            WriteString(writer, "hidden");
            writer.Write((byte)1);

            writer.Write((byte)1);
            WriteString(writer, "preventsChatReports");
            writer.Write((byte)0);

            writer.Write((byte)8);
            WriteString(writer, "ip");
            WriteString(writer, "192.168.31.111");

            writer.Write((byte)8);
            WriteString(writer, "name");
            WriteString(writer, "Minecraft永劫服务器");

            writer.Write((byte)0);
            writer.Write((byte)0);
        }

        var entry = Assert.Single(service.Load(temp.Path));
        Assert.Equal("Minecraft永劫服务器", entry.Name);
        Assert.Equal("192.168.31.111", entry.Address);
    }

    [Fact]
    public void 旧版小端文件会被读取并转换为标准格式()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);
        var path = Path.Combine(temp.Path, ServerListService.FileName);

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write((byte)10);
            WriteLegacyString(writer, string.Empty);

            writer.Write((byte)9);
            WriteLegacyString(writer, "servers");
            writer.Write((byte)10);
            writer.Write(1);

            writer.Write((byte)8);
            WriteLegacyString(writer, "name");
            WriteLegacyString(writer, "旧版服务器");

            writer.Write((byte)8);
            WriteLegacyString(writer, "ip");
            WriteLegacyString(writer, "old.example.com:25565");

            writer.Write((byte)0);
            writer.Write((byte)0);
        }

        var entry = Assert.Single(service.Load(temp.Path));
        Assert.Equal("旧版服务器", entry.Name);
        Assert.Equal("old.example.com:25565", entry.Address);

        // 读完后应已改写为大端文件，游戏才能正常识别。
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x00, 0x09, 0x00, 0x07 }, bytes[..6]);
    }

    [Fact]
    public void 保存的文件使用大端NBT布局()
    {
        using var temp = new TempDirectory();
        var service = Create(out _);
        var entries = new[]
        {
            new MinecraftServerEntry { Name = "Test", Address = "mc.example.com" },
        };

        Assert.True(service.Save(temp.Path, entries));

        var bytes = File.ReadAllBytes(ServerListService.GetPath(temp.Path));

        // 根 compound、列表名长度、元素类型与数量都必须是 NBT 规定的大端序。
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x00, 0x09, 0x00, 0x07 }, bytes[..6]);
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x00, 0x00, 0x01 }, bytes[13..18]);
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
        writer.Write((byte)(bytes.Length >> 8));
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteLegacyString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteInt32(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
