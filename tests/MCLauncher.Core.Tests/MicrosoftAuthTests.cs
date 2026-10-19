using System.Net;
using System.Security.Cryptography;
using System.Text;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Services.Auth;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

public sealed class MicrosoftAuthTests
{
    private static MicrosoftAuthService CreateService(out TestLog log, out ConfigService config, out HttpDownloader downloader, string? clientId = null, string? redirectUri = null)
    {
        log = new TestLog();
        var temp = new TempDirectory();
        var store = new JsonConfigStore(temp.Path, log);
        config = new ConfigService(store, log);
        config.Load();

        if (clientId is not null)
        {
            config.Settings.MsClientId = clientId;
        }

        if (redirectUri is not null)
        {
            config.Settings.MsRedirectUri = redirectUri;
        }

        downloader = new HttpDownloader(log, new FakeHttpHandler("{}"));
        return new MicrosoftAuthService(downloader, config, log);
    }

    /// <summary>
    /// 这是踩过的坑：回调地址多一个 <c>/auth</c> 路径，Azure 就会直接返回
    /// <c>invalid_request: redirect_uri is not valid</c>，因为「注册 http://localhost 匹配任意端口」
    /// 这条通配规则只覆盖到端口，不覆盖路径。这里把"不得带路径"写死成回归测试。
    /// </summary>
    [Fact]
    public void 回调地址必须不带路径()
    {
        var log = new TestLog();
        using var listener = new LoopbackAuthListener(39123, log);

        Assert.Equal("http://localhost:39123", listener.RedirectUri);
        Assert.DoesNotContain("/auth", listener.RedirectUri);
        Assert.Equal(39123, listener.Port);
    }

    [Fact]
    public void 指定回调地址时原样使用()
    {
        var log = new TestLog();
        using var listener = new LoopbackAuthListener(39123, log, "http://localhost:39123/signin");

        Assert.Equal("http://localhost:39123/signin", listener.RedirectUri);
    }

    [Fact]
    public void 授权地址包含全部必需参数()
    {
        var url = MsAuthEndpoints.Consumers.BuildAuthorizeUrl(
            "client-123",
            "http://localhost:39123",
            "challenge-abc",
            "state-xyz");

        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=client-123", url);
        Assert.Contains("code_challenge=challenge-abc", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=state-xyz", url);
        Assert.Contains("XboxLive.signin", url);
        // 回调地址必须被正确转义
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A39123", url);
    }

    [Fact]
    public void PKCE的challenge等于verifier的SHA256的base64url()
    {
        var verifier = PkceUtil.CreateVerifier();
        var challenge = PkceUtil.CreateChallenge(verifier);

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expected, challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
        Assert.DoesNotContain('=', challenge);
        Assert.InRange(verifier.Length, 43, 128);
    }

    [Fact]
    public void 未配置客户端ID时给出引导而不是崩溃()
    {
        var service = CreateService(out _, out _, out var downloader);
        using (downloader)
        {
            Assert.False(service.IsConfigured);

            var exception = Assert.Throws<MsAuthException>(() =>
                service.LoginAsync(_ => Task.CompletedTask).GetAwaiter().GetResult());

            Assert.Equal(MsAuthFailure.NotConfigured, exception.Failure);
            Assert.Contains("客户端 ID", exception.ToUserMessage());
        }
    }

    [Fact]
    public void 回调注册指引区分自动与固定两种情形()
    {
        var automatic = CreateService(out _, out _, out var downloader1, clientId: "cid");
        using (downloader1)
        {
            var text = automatic.DescribeRedirectSetup();
            Assert.Contains("http://localhost", text);
            // 自动模式下要强调"不带端口、不带路径"
            Assert.Contains("不带端口", text);
        }

        var pinned = CreateService(out _, out _, out var downloader2, clientId: "cid", redirectUri: "http://localhost:39123");
        using (downloader2)
        {
            var text = pinned.DescribeRedirectSetup();
            Assert.Contains("http://localhost:39123", text);
            Assert.Contains("固定回调地址", text);
        }
    }

    [Fact]
    public void 错误码映射成人话()
    {
        Assert.Contains("封禁", new MsAuthException(MsAuthFailure.AccountBanned, "").ToUserMessage());
        Assert.Contains("xbox.com", new MsAuthException(MsAuthFailure.NoXboxAccount, "").ToUserMessage());
        Assert.Contains("地区", new MsAuthException(MsAuthFailure.RegionBlocked, "").ToUserMessage());
        Assert.Contains("家庭组", new MsAuthException(MsAuthFailure.ChildAccount, "").ToUserMessage());
        Assert.Contains("没有购买", new MsAuthException(MsAuthFailure.NoGameOwnership, "").ToUserMessage());
        Assert.Contains("重新登录", new MsAuthException(MsAuthFailure.MustSignInAgain, "").ToUserMessage());
        Assert.Contains("稍后", new MsAuthException(MsAuthFailure.RateLimited, "").ToUserMessage());
    }

    [Fact]
    public void 两套端点都可构造授权地址()
    {
        foreach (var endpoints in MsAuthEndpoints.All)
        {
            var url = endpoints.BuildAuthorizeUrl("cid", "http://localhost:1", "c", "s");
            Assert.StartsWith(endpoints.AuthorizeUrl, url, StringComparison.Ordinal);
            Assert.Equal(endpoints, MsAuthEndpoints.ByName(endpoints.Name));
        }
    }

    [Fact]
    public void 只有账号中心端点支持设备码()
    {
        Assert.True(MsAuthEndpoints.Consumers.SupportsDeviceCode);
        Assert.NotNull(MsAuthEndpoints.Consumers.DeviceCodeUrl);
        Assert.EndsWith("/devicecode", MsAuthEndpoints.Consumers.DeviceCodeUrl!, StringComparison.Ordinal);

        Assert.False(MsAuthEndpoints.Live.SupportsDeviceCode);
        Assert.Null(MsAuthEndpoints.Live.DeviceCodeUrl);
    }

    /// <summary>
    /// 设备码流程的关键中间态：令牌端点在用户还没授权时返回 HTTP 400 + authorization_pending，
    /// 这**不是失败**，必须继续轮询。这里用一个按顺序应答的假处理器把整条链走通。
    /// </summary>
    [Fact]
    public async Task 设备码流程在授权前会持续轮询并最终登录成功()
    {
        var log = new TestLog();
        var temp = new TempDirectory();
        var store = new JsonConfigStore(temp.Path, log);
        var config = new ConfigService(store, log);
        config.Load();
        config.Settings.MsClientId = "client-123";

        var handler = new SequencedHttpHandler(
            // 1. 申请设备码
            (HttpStatusCode.OK, """
            {"device_code":"dev-code","user_code":"ABCD-EFGH",
             "verification_uri":"https://microsoft.com/devicelogin","expires_in":60,"interval":1,
             "message":"请在浏览器中输入代码"}
            """),
            // 2. 第一次轮询：用户还没授权
            (HttpStatusCode.BadRequest, """{"error":"authorization_pending"}"""),
            // 3. 第二次轮询：拿到 MSA 令牌
            (HttpStatusCode.OK, """{"access_token":"msa-token","refresh_token":"refresh-1","expires_in":3600}"""),
            // 4. Xbox Live
            (HttpStatusCode.OK, """{"Token":"xbl-token","DisplayClaims":{"xui":[{"uhs":"user-hash"}]}}"""),
            // 5. XSTS
            (HttpStatusCode.OK, """{"Token":"xsts-token","DisplayClaims":{"xui":[{"uhs":"user-hash"}]}}"""),
            // 6. Minecraft 登录
            (HttpStatusCode.OK, """{"access_token":"mc-token","expires_in":86400}"""),
            // 7. 游戏所有权
            (HttpStatusCode.OK, """{"items":[{"name":"product_minecraft"}]}"""),
            // 8. 玩家档案
            (HttpStatusCode.OK, """{"id":"5627dd98e6be3c21b8a8e92344183641","name":"Steve"}"""));

        using var downloader = new HttpDownloader(log, handler);
        var service = new MicrosoftAuthService(downloader, config, log);

        MsDeviceCodePrompt? shown = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        var account = await service.LoginWithDeviceCodeAsync(
            new InlineProgress<MsDeviceCodePrompt>(p => shown = p),
            null,
            cts.Token);

        // 短码必须被展示出来，否则用户无从输入
        Assert.NotNull(shown);
        Assert.Equal("ABCD-EFGH", shown!.UserCode);
        Assert.Equal("https://microsoft.com/devicelogin", shown.VerificationUri);

        Assert.Equal(AccountKind.Microsoft, account.Kind);
        Assert.Equal("Steve", account.Name);
        Assert.Equal("5627dd98e6be3c21b8a8e92344183641", account.Uuid);
        Assert.Equal("user-hash", account.Xuid);
        Assert.Equal("refresh-1", account.RefreshToken);

        // 第二次轮询必须带上 device_code 授权类型
        var pollBody = handler.Bodies.FirstOrDefault(b => b.Contains("device_code", StringComparison.Ordinal));
        Assert.NotNull(pollBody);
        Assert.Contains("urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", pollBody);
    }

    private const string DeviceCodeJson = """
    {"device_code":"dev","user_code":"CODE-1","verification_uri":"https://microsoft.com/devicelogin",
     "expires_in":120,"interval":1}
    """;

    private const string MsaTokenJson = """{"access_token":"msa","refresh_token":"rt","expires_in":3600,"scope":"XboxLive.signin offline_access"}""";

    /// <summary>
    /// 如果微软只授予了别的 scope（表现为授权页没勾 Xbox 权限），报错必须直接点出来，
    /// 而不是让用户去怀疑"客户端 ID 没进白名单"这种不存在的前提。
    /// </summary>
    [Fact]
    public async Task 未授予XboxLive签名权限时直接点明原因()
    {
        var (service, downloader, _) = CreateWithHandler(
            (HttpStatusCode.OK, DeviceCodeJson),
            (HttpStatusCode.OK, """{"access_token":"msa","expires_in":3600,"scope":"openid profile offline_access"}"""));

        using (downloader)
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
        {
            var ex = await Assert.ThrowsAsync<MsAuthException>(() =>
                service.LoginWithDeviceCodeAsync(null, null, cts.Token));

            Assert.Contains("XboxLive.signin", ex.Message);
            Assert.Contains("API 权限", ex.Message);
        }
    }

    /// <summary>
    /// 真实踩到的坑：Mojang 对没进允许清单的新应用注册回 403 +
    /// "Invalid app registration, see https://aka.ms/AppRegInfo"。
    /// 这条必须被单独识别 —— 它既不是账号问题也不是配置写错，而是要提交白名单审核。
    /// </summary>
    [Fact]
    public async Task Mojang拒绝未获许可的应用注册时给出白名单审核指引()
    {
        const string XboxTokenJson = """{"Token":"xbl","DisplayClaims":{"xui":[{"uhs":"user-hash"}]}}""";

        var (service, downloader, _) = CreateWithHandler(
            (HttpStatusCode.OK, DeviceCodeJson),
            (HttpStatusCode.OK, MsaTokenJson),
            (HttpStatusCode.OK, XboxTokenJson),
            (HttpStatusCode.OK, XboxTokenJson),
            (HttpStatusCode.Forbidden,
                """{"path":"/authentication/login_with_xbox","errorMessage":"Invalid app registration, see https://aka.ms/AppRegInfo for more information"}"""));

        using (downloader)
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
        {
            var ex = await Assert.ThrowsAsync<MsAuthException>(() =>
                service.LoginWithDeviceCodeAsync(null, null, cts.Token));

            Assert.Equal(MsAuthFailure.AppNotApproved, ex.Failure);

            var message = ex.ToUserMessage();
            Assert.Contains("mce-reviewappid", message);
            Assert.Contains("允许清单", message);
            // 必须明确告诉用户：既有启动器免申请不代表新应用免申请
            Assert.Contains("历史保留", message);
        }
    }

    private static (MicrosoftAuthService Service, HttpDownloader Downloader, TestLog Log) CreateWithHandler(params (HttpStatusCode, string)[] responses)
    {
        var log = new TestLog();
        var temp = new TempDirectory();
        var store = new JsonConfigStore(temp.Path, log);
        var config = new ConfigService(store, log);
        config.Load();
        config.Settings.MsClientId = "client-123";

        var downloader = new HttpDownloader(log, new SequencedHttpHandler(responses));
        return (new MicrosoftAuthService(downloader, config, log), downloader, log);
    }

    /// <summary>
    /// Xbox Live 这一步同样会回 XErr —— 以前我把它当通用 HTTP 失败处理，
    /// 结果"没有 Xbox 档案"被糊成"微软拒绝了这次请求"，完全没法排查。这里锁死映射。
    /// </summary>
    [Fact]
    public async Task XboxLive的XErr被映射成具体原因()
    {
        var (service, downloader, _) = CreateWithHandler(
            (HttpStatusCode.OK, DeviceCodeJson),
            (HttpStatusCode.OK, MsaTokenJson),
            (HttpStatusCode.Unauthorized, """{"Identity":"0","XErr":2148916233,"Message":"无 Xbox 档案"}"""),
            (HttpStatusCode.Unauthorized, """{"Identity":"0","XErr":2148916233,"Message":"无 Xbox 档案"}"""));

        using (downloader)
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
        {
            var ex = await Assert.ThrowsAsync<MsAuthException>(() =>
                service.LoginWithDeviceCodeAsync(null, null, cts.Token));

            Assert.Equal(MsAuthFailure.NoXboxAccount, ex.Failure);
            Assert.Contains("xbox.com", ex.ToUserMessage());
        }
    }

    /// <summary>无法归类的 401/403 必须把"哪个接口 + 服务端原话"带出来，否则无从排查。</summary>
    [Fact]
    public async Task 通用401的报错带上接口与服务端原话()
    {
        var (service, downloader, _) = CreateWithHandler(
            (HttpStatusCode.OK, DeviceCodeJson),
            (HttpStatusCode.OK, MsaTokenJson),
            (HttpStatusCode.Unauthorized, """{"error":"invalid_token","error_description":"ticket rejected"}"""));

        using (downloader)
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
        {
            var ex = await Assert.ThrowsAsync<MsAuthException>(() =>
                service.LoginWithDeviceCodeAsync(null, null, cts.Token));

            Assert.Contains("user.auth.xboxlive.com", ex.Message);
            Assert.Contains("invalid_token", ex.Message);
            Assert.Contains("401", ex.Message);
        }
    }

    private sealed class SequencedHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

        public SequencedHttpHandler(params (HttpStatusCode Status, string Body)[] responses) =>
            _responses = new Queue<(HttpStatusCode, string)>(responses);

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            // 最后一条会被反复复用，方便模拟"一直 pending"这类场景。
            var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();

            return new HttpResponseMessage(response.Status)
            {
                RequestMessage = request,
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public void 微软账号的类型与协议字段()
    {
        var account = new Account
        {
            Kind = AccountKind.Microsoft,
            Name = "Steve",
            Uuid = "5627dd98e6be3c21b8a8e92344183641",
            Xuid = "123",
            ClientId = "cid",
            ExpiresAt = DateTimeOffset.Now.AddHours(1),
        };

        Assert.Equal("msa", account.UserType);
        Assert.False(account.IsExpired(TimeSpan.FromMinutes(5)));
    }
}
