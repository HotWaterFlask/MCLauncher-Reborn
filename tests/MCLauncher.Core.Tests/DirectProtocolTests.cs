using System.Net;
using System.Net.Sockets;
using System.Text;
using MCLauncher.Core.Connect;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

public sealed class DirectProtocolTests
{
    [Theory]
    [InlineData("8.8.8.8", Ipv4Scope.Public)]
    [InlineData("1.1.1.1", Ipv4Scope.Public)]
    [InlineData("192.168.1.10", Ipv4Scope.Private)]
    [InlineData("10.0.0.5", Ipv4Scope.Private)]
    [InlineData("172.16.0.1", Ipv4Scope.Private)]
    [InlineData("172.31.255.254", Ipv4Scope.Private)]
    [InlineData("172.32.0.1", Ipv4Scope.Public)]
    [InlineData("100.64.0.1", Ipv4Scope.Cgnat)]
    [InlineData("100.127.255.254", Ipv4Scope.Cgnat)]
    [InlineData("100.128.0.1", Ipv4Scope.Public)]
    [InlineData("127.0.0.1", Ipv4Scope.Reserved)]
    [InlineData("169.254.1.1", Ipv4Scope.Reserved)]
    [InlineData("224.0.0.1", Ipv4Scope.Reserved)]
    [InlineData("192.0.2.10", Ipv4Scope.Reserved)]
    [InlineData("198.51.100.7", Ipv4Scope.Reserved)]
    [InlineData("203.0.113.9", Ipv4Scope.Reserved)]
    [InlineData("240.0.0.1", Ipv4Scope.Reserved)]
    [InlineData("not-an-ip", Ipv4Scope.Reserved)]
    [InlineData("::1", Ipv4Scope.Reserved)]
    public void IPv4地址被正确分类(string address, Ipv4Scope expected)
    {
        Assert.Equal(expected, DirectProtocol.ClassifyIpv4(address));
    }

    [Theory]
    [InlineData("2408:8207:1234:5678::1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002::1", false)]
    [InlineData("2001:0::1", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("not-an-ip", false)]
    public void 只有全球单播IPv6可以入站(string address, bool expected)
    {
        Assert.Equal(expected, DirectProtocol.IsGlobalIpv6(address));
    }

    [Theory]
    [InlineData("192.168.1.10", DirectEndpointKind.Lan)]
    [InlineData("100.64.1.1", null)]
    [InlineData("8.8.8.8", DirectEndpointKind.Ipv4)]
    [InlineData("2408:8207::1", DirectEndpointKind.Ipv6)]
    [InlineData("fe80::1", null)]
    [InlineData("example.com", null)]
    public void 地址类型归类(string host, DirectEndpointKind? expected)
    {
        Assert.Equal(expected, DirectProtocol.ClassifyHost(host));
    }

    [Theory]
    [InlineData("127.0.0.1:25565", "127.0.0.1", 25565)]
    [InlineData("play.example.com", "play.example.com", 25565)]
    [InlineData("play.example.com:25566", "play.example.com", 25566)]
    [InlineData("[::1]:25565", "::1", 25565)]
    [InlineData("[2408:8207::1]", "2408:8207::1", 25565)]
    [InlineData("2408:8207::1", "2408:8207::1", 25565)]
    [InlineData("  mc.example.com:1  ", "mc.example.com", 1)]
    public void 地址解析支持域名与方括号IPv6(string text, string host, int port)
    {
        Assert.True(DirectProtocol.TryParseAddress(text, out var parsedHost, out var parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:abc")]
    [InlineData("[::1]:abc")]
    [InlineData("[::1]x")]
    [InlineData("[]:25565")]
    public void 非法地址被拒绝(string text)
    {
        Assert.False(DirectProtocol.TryParseAddress(text, out _, out _));
    }

    [Fact]
    public void 地址拼接对IPv6加方括号()
    {
        Assert.Equal("mc.example.com:25565", DirectProtocol.FormatAddress("mc.example.com", 25565));
        Assert.Equal("[2408:8207::1]:25566", DirectProtocol.FormatAddress("2408:8207::1", 25566));
    }

    [Theory]
    [InlineData("Started serving on 51234", 51234)]
    [InlineData("Local game hosted on port 50000", 50000)]
    [InlineData("本地游戏已在端口 57231 上开放", 57231)]
    [InlineData("本地游戏已在端口：57232 上开放", 57232)]
    public void 能从游戏日志识别局域网端口(string log, int expected)
    {
        Assert.Equal(expected, DirectProtocol.DetectLanPort(log));
    }

    [Fact]
    public void 日志里多次开放时取最后一次端口()
    {
        const string log = """
        [12:00:00] [Server thread/INFO]: Started serving on 50001
        [12:05:00] [Server thread/INFO]: Started serving on 50002
        [12:10:00] [Server thread/INFO]: Local game hosted on port 50003
        """;

        Assert.Equal(50003, DirectProtocol.DetectLanPort(log));
    }

    [Fact]
    public void 日志没有端口时返回空()
    {
        Assert.Null(DirectProtocol.DetectLanPort("Started serving in LAN"));
        Assert.Null(DirectProtocol.DetectLanPort(string.Empty));
    }

    [Fact]
    public void 邀请码可以完整往返()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(20);
        var invitation = new DirectInvitation
        {
            Name = "生存服",
            MinecraftVersion = "1.20.4",
            Loader = "NeoForge",
            LoaderVersion = "21.1.80",
            Endpoints = new[]
            {
                new DirectEndpoint("192.168.1.10", 51234, DirectEndpointKind.Lan),
                new DirectEndpoint("8.8.8.8", 51234, DirectEndpointKind.Ipv4),
                new DirectEndpoint("2408:8207::1", 51234, DirectEndpointKind.Ipv6),
            },
            ExpiresAt = expires,
        };

        var code = DirectProtocol.EncodeInvitation(invitation);
        Assert.StartsWith(DirectProtocol.InvitationPrefix, code);

        var parsed = DirectProtocol.ParseInvitation(code);
        Assert.Equal("生存服", parsed.Name);
        Assert.Equal("1.20.4", parsed.MinecraftVersion);
        Assert.Equal("NeoForge 21.1.80", parsed.LoaderText);
        Assert.Equal(3, parsed.Endpoints.Count);
        Assert.Equal(DirectEndpointKind.Ipv6, parsed.Endpoints[2].Kind);
        Assert.Equal("[2408:8207::1]:51234", parsed.Endpoints[2].Address);
        Assert.Equal(expires.ToUniversalTime().ToString("o"), parsed.ExpiresAt.ToUniversalTime().ToString("o"));
    }

    [Fact]
    public void 邀请码去重同地址端点()
    {
        var invitation = new DirectInvitation
        {
            Name = "房主",
            MinecraftVersion = "1.21.1",
            Endpoints = new[]
            {
                new DirectEndpoint("8.8.8.8", 25565, DirectEndpointKind.Ipv4),
                new DirectEndpoint("8.8.8.8", 25565, DirectEndpointKind.Ipv4),
            },
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
        };

        var parsed = DirectProtocol.ParseInvitation(DirectProtocol.EncodeInvitation(invitation));
        Assert.Single(parsed.Endpoints);
    }

    [Fact]
    public void 过期邀请被拒绝()
    {
        var invitation = new DirectInvitation
        {
            Name = "房主",
            MinecraftVersion = "1.21.1",
            Endpoints = new[] { new DirectEndpoint("8.8.8.8", 25565, DirectEndpointKind.Ipv4) },
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        var error = Assert.Throws<InvalidDataException>(
            () => DirectProtocol.ParseInvitation(DirectProtocol.EncodeInvitation(invitation)));
        Assert.Contains("过期", error.Message);
    }

    [Fact]
    public void 有效期超过二十五小时的邀请被拒绝()
    {
        var invitation = new DirectInvitation
        {
            Name = "房主",
            MinecraftVersion = "1.21.1",
            Endpoints = new[] { new DirectEndpoint("8.8.8.8", 25565, DirectEndpointKind.Ipv4) },
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(26),
        };

        Assert.Throws<InvalidDataException>(
            () => DirectProtocol.ParseInvitation(DirectProtocol.EncodeInvitation(invitation)));
    }

    [Fact]
    public void 含本机回环地址的邀请被拒绝()
    {
        var now = DateTimeOffset.UtcNow;
        var json = $$"""
        {
          "format": "{{DirectProtocol.InvitationFormat}}",
          "version": {{DirectProtocol.InvitationVersion}},
          "name": "房主",
          "minecraftVersion": "1.21.1",
          "expiresAt": "{{now.AddHours(2).ToString("o")}}",
          "endpoints": [{"host":"127.0.0.1","port":25565}]
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => DirectProtocol.ParseInvitation(json));
        Assert.Contains("不可直连", error.Message);
    }

    [Fact]
    public void 格式错误的邀请被拒绝()
    {
        Assert.Throws<InvalidDataException>(() => DirectProtocol.ParseInvitation(string.Empty));
        Assert.Throws<InvalidDataException>(() => DirectProtocol.ParseInvitation("MCLR-DIRECT-1:@@@@"));
        Assert.Throws<InvalidDataException>(() => DirectProtocol.ParseInvitation("{\"format\":\"OTHER\"}"));
    }

    [Fact]
    public void 端点数量超过上限被拒绝()
    {
        var endpoints = Enumerable
            .Range(1, DirectProtocol.MaxEndpoints + 1)
            .Select(index => new DirectEndpoint($"8.8.8.{index}", 25565, DirectEndpointKind.Ipv4))
            .ToArray();

        var invitation = new DirectInvitation
        {
            Name = "房主",
            MinecraftVersion = "1.21.1",
            Endpoints = endpoints,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
        };

        Assert.Throws<InvalidDataException>(
            () => DirectProtocol.ParseInvitation(DirectProtocol.EncodeInvitation(invitation)));
    }

    [Fact]
    public async Task 端口探测能识别本机监听()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(await DirectProtocol.ProbeTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task 端口探测对未监听端口返回假()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.False(await DirectProtocol.ProbeTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(1)));
        Assert.False(await DirectProtocol.ProbeTcpAsync("127.0.0.1", 0, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task 转发器把字节流原样送到游戏端口()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;

        var echoDone = Task.Run(async () =>
        {
            using var accepted = await upstream.AcceptTcpClientAsync();
            var acceptedStream = accepted.GetStream();
            var buffer = new byte[1024];
            var read = await acceptedStream.ReadAsync(buffer);
            await acceptedStream.WriteAsync(buffer.AsMemory(0, read));
            await acceptedStream.FlushAsync();
        });

        await using var forwarder = LocalTcpForwarder.Start(upstreamPort);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, forwarder.Port);
        var stream = client.GetStream();
        var payload = Encoding.UTF8.GetBytes("hello-minecraft");
        await stream.WriteAsync(payload);
        await stream.FlushAsync();

        var response = new byte[payload.Length];
        var total = 0;
        while (total < response.Length)
        {
            var read = await stream.ReadAsync(response.AsMemory(total));
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        await echoDone;
        Assert.Equal(payload, response);
        Assert.Equal("hello-minecraft", Encoding.UTF8.GetString(response, 0, total));
        Assert.True(forwarder.Port is >= 1 and <= 65535);

        upstream.Stop();
    }
}
