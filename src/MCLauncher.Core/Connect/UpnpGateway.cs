using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Connect;

/// <summary>
/// 极简 UPnP IGD 客户端：SSDP 发现网关、读取 WANIPConnection/WANPPPConnection，
/// 建立与删除 TCP 端口映射。所有请求只发往本地局域网，不使用系统代理。
/// </summary>
public sealed class UpnpGateway
{
    private const string IgdServicePrefix = "urn:schemas-upnp-org:service:";
    private const int MaxDescriptionBytes = 512 * 1024;

    private static readonly HttpClient Http = CreateHttpClient();

    public required Uri ControlUri { get; init; }

    public required string ServiceType { get; init; }

    /// <summary>网关控制页所在的私网地址（用于提示）。</summary>
    public required string ControlHost { get; init; }

    /// <summary>discovery 时读取到的 WAN 地址；读取失败为 null。</summary>
    public string? ExternalAddress { get; private set; }

    /// <summary>SSDP 发现局域网内所有 IGD 网关。</summary>
    public static async Task<IReadOnlyList<UpnpGateway>> DiscoverAsync(ILogger log, CancellationToken ct = default)
    {
        var locations = new List<Uri>();

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var message = "M-SEARCH * HTTP/1.1\r\n"
                          + "HOST: 239.255.255.250:1900\r\n"
                          + "MAN: \"ssdp:discover\"\r\n"
                          + "MX: 2\r\n"
                          + "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
            var payload = Encoding.ASCII.GetBytes(message);
            var multicast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

            await udp.SendAsync(payload, multicast, ct).ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddMilliseconds(2500);
            while (DateTime.UtcNow < deadline)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(remaining);

                try
                {
                    var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    var text = Encoding.ASCII.GetString(response.Buffer);
                    var location = ReadHeader(text, "LOCATION");

                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        locations.Add(uri);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            log.Warn("SSDP 网关发现失败", ex);
        }

        var gateways = new List<UpnpGateway>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in locations.DistinctBy(uri => uri.AbsoluteUri))
        {
            ct.ThrowIfCancellationRequested();

            if (!IPAddress.TryParse(location.Host, out var gatewayIp)
                || DirectProtocol.ClassifyIpv4(location.Host) != Ipv4Scope.Private)
            {
                continue;
            }

            try
            {
                foreach (var gateway in await ReadServicesAsync(location, gatewayIp, ct).ConfigureAwait(false))
                {
                    if (seen.Add(gateway.ControlUri.AbsoluteUri))
                    {
                        gateways.Add(gateway);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                           or XmlException or IOException or InvalidOperationException)
            {
                log.Warn($"读取 UPnP 网关描述失败：{location}", ex);
            }
        }

        foreach (var gateway in gateways)
        {
            try
            {
                gateway.ExternalAddress = await gateway.GetExternalAddressAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                           or TaskCanceledException or XmlException or IOException)
            {
                log.Warn($"读取 UPnP 网关 WAN 地址失败：{gateway.ControlHost}", ex);
            }
        }

        return gateways;
    }

    /// <summary>读取 WAN 侧公网 / 私网地址。</summary>
    public async Task<string?> GetExternalAddressAsync(CancellationToken ct = default)
    {
        var document = await InvokeAsync("GetExternalIPAddress", string.Empty, ct).ConfigureAwait(false);
        var value = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "NewExternalIPAddress")
            ?.Value
            .Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>建立 TCP 端口映射（租约到期后由路由器自动回收）。</summary>
    public async Task AddPortMappingAsync(
        int port,
        string internalClient,
        string description,
        int leaseSeconds,
        CancellationToken ct = default)
    {
        var arguments = "<NewRemoteHost></NewRemoteHost>"
                        + "<NewExternalPort>" + port.ToString(CultureInfo.InvariantCulture) + "</NewExternalPort>"
                        + "<NewProtocol>TCP</NewProtocol>"
                        + "<NewInternalPort>" + port.ToString(CultureInfo.InvariantCulture) + "</NewInternalPort>"
                        + "<NewInternalClient>" + EscapeXml(internalClient) + "</NewInternalClient>"
                        + "<NewEnabled>1</NewEnabled>"
                        + "<NewPortMappingDescription>" + EscapeXml(description) + "</NewPortMappingDescription>"
                        + "<NewLeaseDuration>" + leaseSeconds.ToString(CultureInfo.InvariantCulture) + "</NewLeaseDuration>";

        await InvokeAsync("AddPortMapping", arguments, ct).ConfigureAwait(false);
    }

    public async Task DeletePortMappingAsync(int port, CancellationToken ct = default)
    {
        var arguments = "<NewRemoteHost></NewRemoteHost>"
                        + "<NewExternalPort>" + port.ToString(CultureInfo.InvariantCulture) + "</NewExternalPort>"
                        + "<NewProtocol>TCP</NewProtocol>";

        await InvokeAsync("DeletePortMapping", arguments, ct).ConfigureAwait(false);
    }

    private async Task<XDocument> InvokeAsync(string action, string arguments, CancellationToken ct)
    {
        var envelope = "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                       + "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" "
                       + "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
                       + "<s:Body>"
                       + $"<u:{action} xmlns:u=\"{ServiceType}\">{arguments}</u:{action}>"
                       + "</s:Body></s:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, ControlUri);
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{ServiceType}#{action}\"");
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");

        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeFault(body)
                                                ?? $"UPnP 调用失败（HTTP {(int)response.StatusCode}）");
        }

        return ParseXml(body);
    }

    private static async Task<IReadOnlyList<UpnpGateway>> ReadServicesAsync(
        Uri location,
        IPAddress gatewayIp,
        CancellationToken ct)
    {
        var gateways = new List<UpnpGateway>();

        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var document = ParseXml(stream);

        var baseUri = location;
        var urlBase = document.Root?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "URLBase")
            ?.Value
            .Trim();

        if (!string.IsNullOrWhiteSpace(urlBase)
            && Uri.TryCreate(baseUri, urlBase, out var parsedBase)
            && parsedBase.Host.Equals(location.Host, StringComparison.OrdinalIgnoreCase))
        {
            baseUri = parsedBase;
        }

        foreach (var service in document.Descendants().Where(element => element.Name.LocalName == "service"))
        {
            var serviceType = service.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "serviceType")
                ?.Value
                .Trim();

            if (string.IsNullOrWhiteSpace(serviceType)
                || !serviceType.StartsWith(IgdServicePrefix, StringComparison.OrdinalIgnoreCase)
                || !(serviceType.Contains(":WANIPConnection:", StringComparison.OrdinalIgnoreCase)
                     || serviceType.Contains(":WANPPPConnection:", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var controlUrl = service.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "controlURL")
                ?.Value
                .Trim();

            if (string.IsNullOrWhiteSpace(controlUrl)
                || !Uri.TryCreate(baseUri, controlUrl, out var controlUri))
            {
                continue;
            }

            // 控制地址必须仍指向同一个私网网关，避免被描述文件带到别的主机。
            if (!controlUri.Host.Equals(location.Host, StringComparison.OrdinalIgnoreCase)
                || (controlUri.Scheme != Uri.UriSchemeHttp && controlUri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            gateways.Add(new UpnpGateway
            {
                ControlUri = controlUri,
                ServiceType = serviceType,
                ControlHost = gatewayIp.ToString(),
            });
        }

        return gateways;
    }

    private static XDocument ParseXml(string xml) => ParseXml(XmlReader.Create(
        new StringReader(xml),
        new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDescriptionBytes,
            IgnoreComments = true,
        }));

    private static XDocument ParseXml(Stream stream) => ParseXml(XmlReader.Create(
        stream,
        new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDescriptionBytes,
            IgnoreComments = true,
        }));

    private static XDocument ParseXml(XmlReader reader)
    {
        using (reader)
        {
            return XDocument.Load(reader);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
    }

    private static string? ReadHeader(string text, string name)
    {
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            if (line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(colon + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? DescribeFault(string body)
    {
        try
        {
            var document = ParseXml(body);
            var description = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "errorDescription")
                ?.Value
                .Trim();

            var code = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "errorCode")
                ?.Value
                .Trim();

            return (code, description) switch
            {
                (null or "", null or "") => null,
                (not null, null or "") => $"UPnP 错误 {code}",
                (null or "", not null) => description,
                _ => $"UPnP 错误 {code}：{description}",
            };
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
}
