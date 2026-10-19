using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MCLauncher.Core.Tests.Support;

/// <summary>
/// 可编程的假 HTTP 处理器：用于在没有真实网络的情况下确定性地验证
/// 断点续传（206）、服务器忽略 Range（200）、失败重试、哈希校验等分支。
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly bool _supportsRange;
    private readonly int _failFirstRequests;

    public FakeHttpHandler(string content, bool supportsRange = true, int failFirstRequests = 0)
    {
        _content = Encoding.UTF8.GetBytes(content);
        _supportsRange = supportsRange;
        _failFirstRequests = failFirstRequests;
    }

    public int RequestCount { get; private set; }

    /// <summary>每次请求携带的 Range 头（无则为 null）。</summary>
    public List<string?> RangeHeaders { get; } = new();

    public byte[] Content => _content;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        RangeHeaders.Add(request.Headers.Range?.ToString());

        if (RequestCount <= _failFirstRequests)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = "fake failure",
            });
        }

        var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;

        if (from is { } start && start > 0 && _supportsRange)
        {
            var slice = _content[(int)start..];
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(slice),
            };
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, _content.Length - 1, _content.Length);
            return Task.FromResult(partial);
        }

        // 不支持 Range（或未带 Range）：直接返回完整内容 + 200。
        var full = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(_content),
        };
        full.Content.Headers.ContentLength = _content.Length;
        return Task.FromResult(full);
    }
}
