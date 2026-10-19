using System.Net;

namespace MCLauncher.Core.Network;

/// <summary>
/// 带响应体的 HTTP 失败。微软登录链路上很多错误（XSTS 的 XErr、MC 的 errorMessage）
/// 只有把响应体读出来才知道发生了什么，所以必须带上它。
/// </summary>
public sealed class HttpApiException : Exception
{
    public HttpApiException(HttpStatusCode statusCode, string? responseBody, string? reasonPhrase, string url)
        : base($"HTTP {(int)statusCode} {reasonPhrase ?? string.Empty} → {url}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ReasonPhrase = reasonPhrase;
        Url = url;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    public string? ReasonPhrase { get; }

    public string Url { get; }
}
