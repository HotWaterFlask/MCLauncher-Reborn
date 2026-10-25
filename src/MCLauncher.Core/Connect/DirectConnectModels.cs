namespace MCLauncher.Core.Connect;

/// <summary>直连地址的类型：局域网、公网 IPv4、公网 IPv6。</summary>
public enum DirectEndpointKind
{
    Lan,
    Ipv4,
    Ipv6,
}

/// <summary>IPv4 地址范围。</summary>
public enum Ipv4Scope
{
    Public,
    Private,
    Cgnat,
    Reserved,
}

/// <summary>邀请里的一个候选地址。</summary>
public sealed record DirectEndpoint(string Host, int Port, DirectEndpointKind Kind)
{
    /// <summary>可直接展示与复制的地址（IPv6 自动加方括号）。</summary>
    public string Address => DirectProtocol.FormatAddress(Host, Port);

    public string KindText => Kind switch
    {
        DirectEndpointKind.Ipv6 => "公网 IPv6",
        DirectEndpointKind.Ipv4 => "公网 IPv4",
        _ => "局域网",
    };
}

/// <summary>好友直连邀请（24 小时有效，仅含字面量 IP 与端口）。</summary>
public sealed class DirectInvitation
{
    public string Format { get; init; } = DirectProtocol.InvitationFormat;

    public int Version { get; init; } = 1;

    public required string Name { get; init; }

    public required string MinecraftVersion { get; init; }

    public string? Loader { get; init; }

    public string? LoaderVersion { get; init; }

    public required IReadOnlyList<DirectEndpoint> Endpoints { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public string LoaderText => string.IsNullOrWhiteSpace(Loader) || Loader == "原版"
        ? "原版"
        : string.IsNullOrWhiteSpace(LoaderVersion) ? DisplayLoader(Loader!) : $"{DisplayLoader(Loader!)} {LoaderVersion}";

    private static string DisplayLoader(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "optifine" => "OptiFine",
        _ => loader,
    };
}

/// <summary>本机的一个可用地址。</summary>
public sealed class DirectAddressInfo
{
    public required string InterfaceName { get; init; }

    public required string Address { get; init; }

    public required DirectEndpointKind Kind { get; init; }

    public string Text => $"{InterfaceName}：{Address}（{new DirectEndpoint(Address, 0, Kind).KindText}）";
}

/// <summary>UPnP 网关诊断。</summary>
public sealed class UpnpGatewayInfo
{
    public required string ControlHost { get; init; }

    public string? ExternalAddress { get; init; }

    public required string Diagnosis { get; init; }

    /// <summary>WAN 地址是公网 IPv4 时才能尝试映射。</summary>
    public bool CanMap { get; init; }
}

/// <summary>网络扫描结果。</summary>
public sealed class DirectNetworkInfo
{
    public IReadOnlyList<DirectAddressInfo> Addresses { get; init; } = Array.Empty<DirectAddressInfo>();

    public IReadOnlyList<UpnpGatewayInfo> Gateways { get; init; } = Array.Empty<UpnpGatewayInfo>();

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>开房请求。实例信息只用于生成邀请。</summary>
public sealed class DirectHostRequest
{
    public required string InstanceId { get; init; }

    public required string InstanceName { get; init; }

    /// <summary>读取 <c>logs/latest.log</c> 用；隔离实例与共享目录不同。</summary>
    public required string InstanceDirectory { get; init; }

    public required string MinecraftVersion { get; init; }

    public string? Loader { get; init; }

    public string? LoaderVersion { get; init; }

    /// <summary>游戏内「对局域网开放」的端口；为空时从日志里自动检测。</summary>
    public int? Port { get; init; }

    public bool UseUpnp { get; init; } = true;

    /// <summary>手动填写的公网 IPv4（路由器已手动映射时用）。</summary>
    public string? PublicAddress { get; init; }
}

/// <summary>房间状态快照。</summary>
public sealed class DirectHostState
{
    public bool Active { get; init; }

    public IReadOnlyList<DirectEndpoint> Endpoints { get; init; } = Array.Empty<DirectEndpoint>();

    public string? Invite { get; init; }

    public int LocalPort { get; init; }

    /// <summary>对外监听端口（转发器随机端口）。</summary>
    public int ExposedPort { get; init; }

    public int Connections { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public DateTimeOffset? StartedAt { get; init; }
}

/// <summary>解析邀请的结果。</summary>
public sealed class DirectJoinResult
{
    public required DirectInvitation Invitation { get; init; }

    public DirectEndpoint? Endpoint { get; init; }

    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
}
