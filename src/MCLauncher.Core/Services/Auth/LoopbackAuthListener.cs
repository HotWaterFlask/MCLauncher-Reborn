using System.Net;
using System.Net.Sockets;
using System.Text;
using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Services.Auth;

/// <summary>
/// OAuth 回环回调服务器。
/// <para>
/// 刻意不用 <see cref="System.Net.HttpListener"/>：它在 Windows 上要通过 http.sys，
/// 普通用户没有 URL 保留时会直接 Access Denied。改用裸 <see cref="TcpListener"/> 绑 127.0.0.1，
/// 只实现"读一行请求 + 回一页 HTML"的最小 HTTP，免权限、免防火墙弹窗。
/// </para>
/// </summary>
public sealed class LoopbackAuthListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ILogger _log;
    private readonly string? _redirectUriOverride;
    private bool _disposed;

    /// <param name="port">绑定的回环端口。</param>
    /// <param name="log">日志。</param>
    /// <param name="redirectUriOverride">
    /// 用户在设置里指定的回调地址。指定时原样使用（用户通常是把这一个地址注册进了 Azure），
    /// 为空时用 Azure 桌面应用约定的 <c>http://localhost:{port}</c>。
    /// </param>
    public LoopbackAuthListener(int port, ILogger log, string? redirectUriOverride = null)
    {
        _log = log;
        _redirectUriOverride = string.IsNullOrWhiteSpace(redirectUriOverride) ? null : redirectUriOverride.Trim();
        _listener = new TcpListener(IPAddress.Loopback, port);
        Port = port;
    }

    public int Port { get; }

    /// <summary>
    /// 回环回调地址。
    /// <para>
    /// **必须不带路径**：Azure 对桌面应用的通配规则是"注册 <c>http://localhost</c> 即匹配任意端口"，
    /// 但它只匹配到端口为止 —— 多余的 <c>/auth</c> 会让授权端点直接返回
    /// <c>invalid_request: redirect_uri is not valid</c>。浏览器发起的请求路径是 <c>/</c>，
    /// 我们的处理器本来就是只看 query 不看路径，去掉后缀没有副作用。
    /// </para>
    /// </summary>
    public string RedirectUri => _redirectUriOverride ?? $"http://localhost:{Port}";

    /// <summary>找一个空闲端口（先绑 0 号端口问系统要，再立刻释放）。</summary>
    public static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Start() => _listener.Start();

    /// <summary>
    /// 等浏览器回调。返回授权码。
    /// 会校验 <paramref name="expectedState"/>（防止 CSRF），并在浏览器里渲染一个提示页。
    /// </summary>
    public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"等待微软登录回调超时。如果浏览器页面已经显示了错误（例如 redirect_uri 无效），"
                    + $"说明 Azure 应用里没有把回调地址注册成 {RedirectUri} 所在的形式，"
                    + $"请到 Azure 门户的「身份验证」里添加平台「移动和桌面应用程序」并填入 http://localhost。");
            }

            using (client)
            {
                var result = await HandleAsync(client, expectedState, timeoutCts.Token).ConfigureAwait(false);

                if (result.Code is not null)
                {
                    return result.Code;
                }

                if (result.Error is not null)
                {
                    throw new InvalidOperationException($"登录被拒绝或失败：{result.Error}");
                }

                // 其它请求（浏览器预抓取 favicon 之类）继续等。
            }
        }
    }

    private async Task<(string? Code, string? Error)> HandleAsync(TcpClient client, string expectedState, CancellationToken ct)
    {
        var stream = client.GetStream();

        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine))
        {
            return (null, null);
        }

        // "GET /auth?code=...&state=... HTTP/1.1"
        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            return (null, null);
        }

        var target = parts[1];
        var queryStart = target.IndexOf('?');
        var query = queryStart >= 0 ? ParseQuery(target[(queryStart + 1)..]) : new Dictionary<string, string>();

        query.TryGetValue("code", out var code);
        query.TryGetValue("error", out var error);
        query.TryGetValue("error_description", out var errorDescription);
        query.TryGetValue("state", out var state);

        if (code is null && error is null)
        {
            // 不是回调请求（比如 /favicon.ico），回一个空响应继续等。
            await WriteResponseAsync(stream, "登录中…", "正在等待微软授权完成，可以关闭此页面。", ct).ConfigureAwait(false);
            return (null, null);
        }

        if (error is not null)
        {
            _log.Warn($"微软登录返回错误：{error} {errorDescription}");
            await WriteResponseAsync(stream, "登录失败", errorDescription ?? error, ct).ConfigureAwait(false);
            return (null, errorDescription ?? error);
        }

        // state 必须与发起时一致，否则可能是伪造的回调。
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            _log.Warn("微软登录回调的 state 不匹配，已拒绝");
            await WriteResponseAsync(stream, "登录失败", "回调校验失败（state 不匹配）。", ct).ConfigureAwait(false);
            return (null, "state 不匹配");
        }

        await WriteResponseAsync(stream, "登录成功", "可以关闭此页面，返回启动器继续。", ct).ConfigureAwait(false);
        return (code, null);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separator].Replace('+', ' '));
            var value = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private static async Task WriteResponseAsync(Stream stream, string title, string message, CancellationToken ct)
    {
        // 用占位符而不是内插原始字符串：HTML/CSS 里的花括号和内插语法会打架。
        var html = HtmlTemplate
            .Replace("__TITLE__", WebUtility.HtmlEncode(title))
            .Replace("__MESSAGE__", WebUtility.HtmlEncode(message));

        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {body.Length}\r\n"
            + "Connection: close\r\n\r\n");

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private const string HtmlTemplate = """
    <!doctype html>
    <html lang="zh-CN"><head><meta charset="utf-8"><title>__TITLE__</title>
    <style>
    body{font-family:"Microsoft YaHei UI",sans-serif;background:#f5f5f5;margin:0;
         display:flex;align-items:center;justify-content:center;height:100vh}
    .card{background:#fff;border-radius:10px;padding:32px 44px;text-align:center;
          box-shadow:0 6px 24px rgba(0,0,0,0.13)}
    h1{font-size:20px;margin:0 0 10px}
    p{color:#555;font-size:14px;margin:0}
    </style></head>
    <body><div class="card"><h1>__TITLE__</h1><p>__MESSAGE__</p></div></body></html>
    """;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // 已经停了就忽略。
        }
    }
}
