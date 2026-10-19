namespace MCLauncher.Core.Services.Auth;

/// <summary>
/// 微软登录的端点集合。两套可选：
/// <list type="bullet">
/// <item><b>Consumers</b>（默认）：<c>login.microsoftonline.com/consumers</c>，只需客户端 ID，不需要密码/密钥。</item>
/// <item><b>Live</b>：<c>login.live.com</c>，部分老账号只认这一套。</item>
/// </list>
/// 做成可切换是因为不同账号/租户对两者的接受度不一样，出错时另一套往往是解药。
/// </summary>
public sealed class MsAuthEndpoints
{
    /// <summary>Xbox Live 的 <c>RpsTicket</c> 与 XSTS 的 RelyingParty。</summary>
    public const string XboxLiveRelyingParty = "http://auth.xboxlive.com";

    public const string XstsRelyingParty = "rp://api.minecraftservices.com/";

    public const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";

    public const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    public const string MinecraftEntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";

    private MsAuthEndpoints(string name, string authorizeUrl, string tokenUrl, string scope, string? deviceCodeUrl)
    {
        Name = name;
        AuthorizeUrl = authorizeUrl;
        TokenUrl = tokenUrl;
        Scope = scope;
        DeviceCodeUrl = deviceCodeUrl;
    }

    public string Name { get; }

    public string AuthorizeUrl { get; }

    public string TokenUrl { get; }

    public string Scope { get; }

    /// <summary>
    /// 设备码端点。**只有 Microsoft 账号中心（v2.0）支持设备码流程**，
    /// Live 端点没有对应能力，这里为 null。
    /// </summary>
    public string? DeviceCodeUrl { get; }

    /// <summary>该端点是否支持设备码登录（不需要注册任何回调地址）。</summary>
    public bool SupportsDeviceCode => DeviceCodeUrl is not null;

    /// <summary>微软账号中心（推荐）。</summary>
    public static MsAuthEndpoints Consumers { get; } = new(
        "Microsoft 账号中心",
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize",
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
        "XboxLive.signin offline_access",
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode");

    /// <summary>Live 端点（老账号兜底，不支持设备码）。</summary>
    public static MsAuthEndpoints Live { get; } = new(
        "Live 端点",
        "https://login.live.com/oauth20_authorize.srf",
        "https://login.live.com/oauth20_token.srf",
        "XboxLive.signin offline_access",
        null);

    public static MsAuthEndpoints Default => Consumers;

    public static IReadOnlyList<MsAuthEndpoints> All { get; } = new[] { Consumers, Live };

    /// <summary>按名称取端点集合（配置文件里存的是名称）。</summary>
    public static MsAuthEndpoints ByName(string? name) =>
        All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>拼出授权页地址（含 PKCE 与 state）。</summary>
    public string BuildAuthorizeUrl(string clientId, string redirectUri, string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scope,
            ["state"] = state,
            ["prompt"] = "select_account",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        return AuthorizeUrl + "?" + string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }
}
