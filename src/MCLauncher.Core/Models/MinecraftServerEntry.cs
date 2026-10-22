namespace MCLauncher.Core.Models;

/// <summary>服务器资源包下载策略，对应 servers.dat 的 acceptTextures。</summary>
public enum ServerResourcePackPolicy
{
    /// <summary>进入服务器时询问（0，字段缺省即此值）。</summary>
    Ask = 0,

    /// <summary>总是下载服务器资源包（1）。</summary>
    Always = 1,

    /// <summary>从不下载服务器资源包（2）。</summary>
    Never = 2,
}

/// <summary>servers.dat 里的一条服务器记录。</summary>
public sealed class MinecraftServerEntry
{
    public const string DefaultName = "Minecraft Server";

    public string Name { get; set; } = DefaultName;

    /// <summary>地址，格式为 <c>host</c> 或 <c>host:port</c>。</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>服务器图标（PNG 的 Base64），没有图标时为 null。</summary>
    public string? IconBase64 { get; set; }

    public ServerResourcePackPolicy ResourcePolicy { get; set; } = ServerResourcePackPolicy.Ask;
}
