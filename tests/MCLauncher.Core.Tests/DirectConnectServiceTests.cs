using System.Net;
using System.Net.Sockets;
using MCLauncher.Core.Connect;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

public sealed class DirectConnectServiceTests
{
    private static DirectConnectService Create(out TestLog log)
    {
        log = new TestLog();
        return new DirectConnectService(log);
    }

    [Fact]
    public async Task 没有游戏日志时不返回端口()
    {
        using var temp = new TempDirectory();
        using var service = Create(out _);

        Assert.Null(await service.DetectLanPortAsync(temp.Path));
    }

    [Fact]
    public async Task 从latest_log读取局域网端口()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "logs"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "logs", "latest.log"),
            "[12:34:56] [Server thread/INFO]: Started serving on 61234\n");

        using var service = Create(out _);
        Assert.Equal(61234, await service.DetectLanPortAsync(temp.Path));
    }

    [Fact]
    public async Task 端口未开放时拒绝开房()
    {
        using var temp = new TempDirectory();
        using var service = Create(out _);

        var request = new DirectHostRequest
        {
            InstanceId = "1.21.1",
            InstanceName = "测试实例",
            InstanceDirectory = temp.Path,
            MinecraftVersion = "1.21.1",
            Port = 1,
            UseUpnp = false,
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartHostAsync(request));
        Assert.Contains("端口未开放", error.Message);
        Assert.False(service.State.Active);
    }

    [Fact]
    public async Task 端口开放时可以开启并关闭房间()
    {
        using var temp = new TempDirectory();
        using var service = Create(out _);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var request = new DirectHostRequest
            {
                InstanceId = "1.21.1",
                InstanceName = "测试实例",
                InstanceDirectory = temp.Path,
                MinecraftVersion = "1.21.1",
                Loader = "Fabric",
                LoaderVersion = "0.16.9",
                Port = port,
                UseUpnp = false,
            };

            var state = await service.StartHostAsync(request);

            Assert.True(state.Active);
            Assert.Equal(port, state.LocalPort);
            Assert.NotEmpty(state.Endpoints);
            Assert.False(string.IsNullOrWhiteSpace(state.Invite));

            var invitation = DirectProtocol.ParseInvitation(state.Invite!);
            Assert.Equal("测试实例", invitation.Name);
            Assert.Equal("1.21.1", invitation.MinecraftVersion);
            Assert.Equal("Fabric 0.16.9", invitation.LoaderText);

            var stopped = await service.StopHostAsync();
            Assert.False(stopped.Active);
            Assert.False(string.IsNullOrWhiteSpace(stopped.Messages.FirstOrDefault()));
        }
        finally
        {
            listener.Stop();
        }
    }
}
