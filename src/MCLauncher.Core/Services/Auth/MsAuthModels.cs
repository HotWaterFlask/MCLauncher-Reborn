using System.Text.Json.Serialization;

namespace MCLauncher.Core.Services.Auth;

/// <summary>登录失败原因。界面据此给出可操作的提示，而不是把原始错误码甩给用户。</summary>
public enum MsAuthFailure
{
    /// <summary>没有配置客户端 ID。</summary>
    NotConfigured,

    /// <summary>用户取消 / 关掉了授权页。</summary>
    UserCancelled,

    /// <summary>等授权超时。</summary>
    Timeout,

    /// <summary>该微软账号还没有 Xbox 档案（需要先去 xbox.com 登录一次）。</summary>
    NoXboxAccount,

    /// <summary>账号被封禁。</summary>
    AccountBanned,

    /// <summary>所在地区不支持 Xbox Live。</summary>
    RegionBlocked,

    /// <summary>未成年账号，需要家长账号先加入家庭组。</summary>
    ChildAccount,

    /// <summary>账号被停用。</summary>
    AccountSuspended,

    /// <summary>
    /// Mojang 侧拒绝：这个应用注册不在允许清单里。
    /// Mojang 已要求**新的**第三方应用在访问 Java 版游戏服务 API 前先通过白名单审核。
    /// </summary>
    AppNotApproved,

    /// <summary>这个账号没有买过 Minecraft。</summary>
    NoGameOwnership,

    /// <summary>令牌失效，需要重新登录。</summary>
    MustSignInAgain,

    /// <summary>请求过于频繁。</summary>
    RateLimited,

    /// <summary>微软服务暂时不可用。</summary>
    ServiceUnavailable,

    /// <summary>网络问题。</summary>
    Network,

    Unknown,
}

/// <summary>登录过程中的阶段进度。</summary>
public sealed class MsAuthProgress
{
    public required string Stage { get; init; }

    public string? Detail { get; init; }

    public override string ToString() => Stage;
}

/// <summary>
/// 设备码登录时要在界面上展示给用户的信息。
/// 正常情况下浏览器会直接打开 <see cref="LinkUri"/>（验证码已预填），用户不需要手动输入。
/// </summary>
public sealed class MsDeviceCodePrompt
{
    /// <summary>短码，例如 <c>N5LWRN7E</c>。界面应可选中/复制。</summary>
    public required string UserCode { get; init; }

    /// <summary>验证页根地址（通常是 <c>https://microsoft.com/devicelogin</c>）。</summary>
    public required string VerificationUri { get; init; }

    /// <summary>
    /// 免输入直达链接：<c>https://www.microsoft.com/link?otc=&lt;user_code&gt;</c>。
    /// 打开后验证码已填好，用户只需登录并点「继续」。
    /// </summary>
    public required string LinkUri { get; init; }

    /// <summary>微软给出的整句提示，可直接展示。</summary>
    public string? Message { get; init; }

    /// <summary>该码的有效期（秒）。</summary>
    public int ExpiresInSeconds { get; init; }
}

/// <summary>带语义的登录异常。</summary>
public sealed class MsAuthException : Exception
{
    public MsAuthException(MsAuthFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public MsAuthFailure Failure { get; }

    /// <summary>把失败原因翻译成给用户的下一步建议。</summary>
    public string ToUserMessage() => Failure switch
    {
        MsAuthFailure.NotConfigured => "尚未配置微软客户端 ID。请到「设置」页填写 Azure 应用客户端 ID 后重试。",
        MsAuthFailure.UserCancelled => "已取消登录。",
        // 设备码过期等超时文案由调用方给出，直接透出更有用。
        MsAuthFailure.Timeout => Message,
        MsAuthFailure.NoXboxAccount => "该微软账号还没有 Xbox 档案：请先用它登录一次 xbox.com 创建档案，然后回来重试。",
        MsAuthFailure.AccountBanned => "该 Xbox 账号已被封禁，无法登录。",
        MsAuthFailure.RegionBlocked => "该账号所在地区不支持 Xbox Live。",
        MsAuthFailure.ChildAccount => "这是未成年账号：请让家长账号先把它加入 Microsoft 家庭组（且家长已同意）。",
        MsAuthFailure.AccountSuspended => "该账号已被停用，请到微软账号中心查看。",
        MsAuthFailure.AppNotApproved =>
            "Mojang 拒绝了这次登录：该 Azure 应用注册不在 Java 版游戏服务 API 的允许清单里。\n"
            + "Mojang 现在要求访问 Java 版游戏服务 API 的**新应用**先提交审核"
            + "（https://aka.ms/mce-reviewappid ，每周审核一次，重复提交不会更快）。\n"
            + "既有的启动器属于历史保留、无需中断，所以不能参照它们认为新应用也不用申请。\n"
            + "审核通过前，请先用离线账号启动游戏。",
        MsAuthFailure.NoGameOwnership => "该微软账号没有购买 Minecraft：Java 版，无法用作正版登录。",
        MsAuthFailure.MustSignInAgain => "登录状态已失效，请重新登录。",
        MsAuthFailure.RateLimited => "请求过于频繁，请稍后再试。",
        MsAuthFailure.ServiceUnavailable => "微软服务暂时不可用，请稍后再试。",
        MsAuthFailure.Network => "网络异常，请检查网络后重试。",
        _ => Message,
    };
}

// ---------------------------------------------------------------- 线上响应模型

/// <summary>OAuth token 端点响应。</summary>
public sealed class MsTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    /// <summary>
    /// 实际授予的 scope。**这是排查 Xbox 换链失败的关键证据**：
    /// 如果这里没有 <c>XboxLive.signin</c>，说明授权页压根没同意这个权限，
    /// 后面的 Xbox Live / XSTS 必然被拒 —— 与"客户端 ID 没白名单"毫无关系。
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>设备码端点响应。</summary>
public sealed class MsDeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("user_code")]
    public string? UserCode { get; set; }

    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; set; }

    /// <summary>
    /// 带验证码的完整验证地址。RFC 8628 里的可选字段，微软通常不返回，
    /// 此时由启动器自行拼 <c>https://www.microsoft.com/link?otc=...</c>。
    /// </summary>
    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>微软要求的最小轮询间隔（秒）。</summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>Xbox Live / XSTS 响应中的 DisplayClaims。</summary>
public sealed class XblDisplayClaims
{
    [JsonPropertyName("xui")]
    public List<Dictionary<string, string>>? Xui { get; set; }
}

public sealed class XblTokenResponse
{
    [JsonPropertyName("Token")]
    public string? Token { get; set; }

    [JsonPropertyName("DisplayClaims")]
    public XblDisplayClaims? DisplayClaims { get; set; }

    /// <summary>XSTS 专有错误码。</summary>
    [JsonPropertyName("XErr")]
    public long XErr { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}

/// <summary><c>login_with_xbox</summary> 的响应。</summary>
public sealed class MinecraftTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>玩家名（该端点也会返回，但以 profile 为准）。</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

public sealed class MinecraftProfile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class MinecraftEntitlements
{
    [JsonPropertyName("items")]
    public List<MinecraftEntitlementItem>? Items { get; set; }
}

public sealed class MinecraftEntitlementItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
