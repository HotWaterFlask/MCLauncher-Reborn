using System.Text.Json.Serialization;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Models;

/// <summary>账号类型。</summary>
public enum AccountKind
{
    /// <summary>离线账号：UUID 由玩家名派生，token 用占位值。</summary>
    Offline,

    /// <summary>微软正版账号。</summary>
    Microsoft,
}

/// <summary>
/// 启动用的账号。离线账号的 UUID 必须按协议派生：
/// <c>UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))</c> —— 即 MD5 后按 version 3 规范化，
/// 自己随便编一个会导致皮肤/统计/成就全部异常。
/// </summary>
public sealed class Account
{
    public AccountKind Kind { get; set; } = AccountKind.Offline;

    /// <summary>玩家名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>UUID（32 位十六进制，无横杠）。</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>访问令牌。离线账号使用占位值。</summary>
    public string AccessToken { get; set; } = "0";

    /// <summary>Xbox 用户 ID（仅微软账号有）。</summary>
    public string? Xuid { get; set; }

    /// <summary>微软 OAuth 刷新令牌（仅微软账号有）。</summary>
    public string? RefreshToken { get; set; }

    /// <summary>令牌过期时间。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore]
    public bool IsMicrosoft => Kind == AccountKind.Microsoft;

    /// <summary>协议里的 <c>${user_type}</c>：微软账号 <c>msa</c>，离线账号 <c>legacy</c>。</summary>
    [JsonIgnore]
    public string UserType => IsMicrosoft ? "msa" : "legacy";

    /// <summary>微软账号的客户端 ID（对应 <c>${clientid}</c>）。</summary>
    public string? ClientId { get; set; }

    public bool IsExpired(TimeSpan margin) =>
        ExpiresAt is not null && ExpiresAt.Value - margin <= DateTimeOffset.Now;

    public static Account CreateOffline(string name, string? uuid = null) => new()
    {
        Kind = AccountKind.Offline,
        Name = name,
        Uuid = string.IsNullOrWhiteSpace(uuid) ? HashUtil.OfflinePlayerUuid(name) : uuid!,
        AccessToken = "0",
    };

    public override string ToString() => $"{Name}（{(IsMicrosoft ? "微软" : "离线")}）";
}
