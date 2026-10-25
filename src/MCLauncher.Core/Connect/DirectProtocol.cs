using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MCLauncher.Core.Connect;

/// <summary>
/// 好友直连的纯协议逻辑：地址分类、局域网端口检测、邀请码编解码、TCP 可达性探测。
/// 不依赖任何第三方服务，也不做 DNS 解析。
/// </summary>
public static class DirectProtocol
{
    public const string InvitationPrefix = "MCLR-DIRECT-1:";

    public const string InvitationFormat = "MCLR-DIRECT";

    public const int InvitationVersion = 1;

    public const int MaxInvitationChars = 16 * 1024;

    public const int MaxEndpoints = 12;

    public const int DefaultPort = 25565;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly Regex LanPortPattern = new(
        @"(?:Started serving on|Local game hosted on port|本地游戏已在端口)[\s:：]*(\d{1,5})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>判断 IPv4 属于公网、私网、运营商级 NAT 还是保留地址。</summary>
    public static Ipv4Scope ClassifyIpv4(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return Ipv4Scope.Reserved;
        }

        var b = ip.GetAddressBytes();
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
        {
            return Ipv4Scope.Cgnat;
        }

        if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))
        {
            return Ipv4Scope.Private;
        }

        if (b[0] == 0 || b[0] == 127 || b[0] >= 224
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2))
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19 || (b[1] == 51 && b[2] == 100)))
            || (b[0] == 203 && b[1] == 0 && b[2] == 113))
        {
            return Ipv4Scope.Reserved;
        }

        return Ipv4Scope.Public;
    }

    /// <summary>是否是可入站的公网 IPv6（拒绝链路本地、ULA、文档地址与隧道地址）。</summary>
    public static bool IsGlobalIpv6(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            return false;
        }

        var b = ip.GetAddressBytes();

        // 2000::/3 之外的地址都不是全球单播。
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        // 2001:db8::/32 文档地址、2002::/16 6to4、3fff::/20 文档地址。
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
        {
            return false;
        }

        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return false;
        }

        if (b[0] == 0x3F && b[1] == 0xFF && (b[2] & 0xF0) == 0x00)
        {
            return false;
        }

        // 2001:0000::/32 是 Teredo。
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
        {
            return false;
        }

        return true;
    }

    /// <summary>把字面量 IP 归类成邀请里可用的地址类型；不可用返回 null。</summary>
    public static DirectEndpointKind? ClassifyHost(string host)
    {
        if (!IPAddress.TryParse(host, out var ip))
        {
            return null;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IsGlobalIpv6(host) ? DirectEndpointKind.Ipv6 : null;
        }

        return ClassifyIpv4(host) switch
        {
            Ipv4Scope.Public => DirectEndpointKind.Ipv4,
            Ipv4Scope.Private => DirectEndpointKind.Lan,
            _ => null,
        };
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    /// <summary>拼出可展示、可直接粘贴给 Minecraft 的地址。</summary>
    public static string FormatAddress(string host, int port) =>
        host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";

    /// <summary>
    /// 解析「host」「host:port」「[ipv6]:port」；缺少端口时返回默认 25565。
    /// 域名与 IPv6 都允许，供 FRP 远程地址使用。
    /// </summary>
    public static bool TryParseAddress(string text, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;

        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close <= 1)
            {
                return false;
            }

            host = value[1..close];
            var rest = value[(close + 1)..];
            if (rest.StartsWith(':'))
            {
                rest = rest[1..];
            }
            else if (rest.Length > 0)
            {
                return false;
            }

            if (rest.Length > 0 && (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
                                    || !IsValidPort(port)))
            {
                return false;
            }

            return host.Length > 0 && !host.Any(c => char.IsWhiteSpace(c));
        }

        var first = value.IndexOf(':');
        var last = value.LastIndexOf(':');
        if (first > 0 && first == last)
        {
            var portText = value[(last + 1)..];
            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
                || !IsValidPort(port))
            {
                return false;
            }

            host = value[..last];
            return host.Length > 0 && !host.Any(c => char.IsWhiteSpace(c));
        }

        // 没有端口：可能是域名，也可能是裸 IPv6。
        host = value;
        return host.Length > 0 && !host.Any(c => char.IsWhiteSpace(c));
    }

    /// <summary>从 <c>latest.log</c> 里找最后一次「对局域网开放」的端口。</summary>
    public static int? DetectLanPort(string logText)
    {
        if (string.IsNullOrEmpty(logText))
        {
            return null;
        }

        int? found = null;
        foreach (Match match in LanPortPattern.Matches(logText))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
                && IsValidPort(port))
            {
                found = port;
            }
        }

        return found;
    }

    public static string EncodeInvitation(DirectInvitation invitation)
    {
        var dto = new InvitationDto
        {
            Format = InvitationFormat,
            Version = InvitationVersion,
            Name = invitation.Name,
            MinecraftVersion = invitation.MinecraftVersion,
            Loader = invitation.Loader,
            LoaderVersion = invitation.LoaderVersion,
            ExpiresAt = invitation.ExpiresAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            Endpoints = invitation.Endpoints
                .Select(endpoint => new EndpointDto { Host = endpoint.Host, Port = endpoint.Port })
                .ToList(),
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        return InvitationPrefix + Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>解析邀请；格式、有效期或地址不合法时抛出带中文说明的异常。</summary>
    public static DirectInvitation ParseInvitation(string input, DateTimeOffset? now = null)
    {
        var value = (input ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException("请先粘贴好友发来的邀请信息。");
        }

        if (value.Length > MaxInvitationChars)
        {
            throw new InvalidDataException("邀请内容过大，请确认粘贴的是完整邀请。");
        }

        string json;
        if (value.StartsWith(InvitationPrefix, StringComparison.Ordinal))
        {
            var bytes = Base64UrlDecode(value[InvitationPrefix.Length..])
                        ?? throw new InvalidDataException("邀请内容不是有效的 Base64，请重新复制。");
            json = Encoding.UTF8.GetString(bytes);
        }
        else
        {
            json = value;
        }

        InvitationDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<InvitationDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("邀请内容无法解析，请让好友重新生成。");
        }

        if (dto is null || dto.Format != InvitationFormat || dto.Version != InvitationVersion)
        {
            throw new InvalidDataException("邀请格式或版本不受支持。");
        }

        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 128
            || string.IsNullOrWhiteSpace(dto.MinecraftVersion) || dto.MinecraftVersion.Length > 128)
        {
            throw new InvalidDataException("邀请里的实例信息不完整。");
        }

        if (dto.Loader is { Length: > 128 } || dto.LoaderVersion is { Length: > 128 })
        {
            throw new InvalidDataException("邀请里的加载器信息过长。");
        }

        if (!DateTimeOffset.TryParse(
                dto.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires))
        {
            throw new InvalidDataException("邀请的有效期无法识别。");
        }

        var current = now ?? DateTimeOffset.UtcNow;
        if (expires <= current || expires > current.AddHours(25))
        {
            throw new InvalidDataException("邀请已过期或有效期异常，请让好友重新生成。");
        }

        if (dto.Endpoints is not { Count: > 0 and <= MaxEndpoints })
        {
            throw new InvalidDataException("邀请里没有可用地址。");
        }

        var endpoints = new List<DirectEndpoint>();
        foreach (var endpoint in dto.Endpoints)
        {
            if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Host) || !IsValidPort(endpoint.Port))
            {
                throw new InvalidDataException("邀请里的地址或端口不合法。");
            }

            var kind = ClassifyHost(endpoint.Host.Trim())
                       ?? throw new InvalidDataException($"邀请含有不可直连的地址：{endpoint.Host}");

            var host = endpoint.Host.Trim();
            if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                host = ip.ToString();
            }

            if (!endpoints.Any(existing => existing.Host == host && existing.Port == endpoint.Port))
            {
                endpoints.Add(new DirectEndpoint(host, endpoint.Port, kind));
            }
        }

        if (endpoints.Count == 0)
        {
            throw new InvalidDataException("邀请里没有可用地址。");
        }

        return new DirectInvitation
        {
            Format = InvitationFormat,
            Version = InvitationVersion,
            Name = dto.Name.Trim(),
            MinecraftVersion = dto.MinecraftVersion.Trim(),
            Loader = string.IsNullOrWhiteSpace(dto.Loader) ? null : dto.Loader.Trim(),
            LoaderVersion = string.IsNullOrWhiteSpace(dto.LoaderVersion) ? null : dto.LoaderVersion.Trim(),
            Endpoints = endpoints,
            ExpiresAt = expires,
        };
    }

    /// <summary>TCP 探测；超时或拒绝都只返回 false。</summary>
    public static async Task<bool> ProbeTcpAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (!IsValidPort(port))
        {
            return false;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string text)
    {
        var value = text.Replace('-', '+').Replace('_', '/');
        value += (value.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => string.Empty,
        };

        if (value.Length % 4 != 0)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class InvitationDto
    {
        public string? Format { get; set; }

        public int Version { get; set; }

        public string? Name { get; set; }

        public string? MinecraftVersion { get; set; }

        public string? Loader { get; set; }

        public string? LoaderVersion { get; set; }

        public string? ExpiresAt { get; set; }

        public List<EndpointDto>? Endpoints { get; set; }
    }

    private sealed class EndpointDto
    {
        public string? Host { get; set; }

        public int Port { get; set; }
    }
}
