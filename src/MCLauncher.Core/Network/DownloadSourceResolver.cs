using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Network;

/// <summary>下载源偏好。</summary>
public enum DownloadSourcePreference
{
    /// <summary>自动：探测镜像是否可用，可用时镜像优先，否则官方优先。</summary>
    Auto,

    /// <summary>尽量使用官方源，失败后回退镜像。</summary>
    Official,

    /// <summary>尽量使用镜像源，失败后回退官方。</summary>
    Mirror,
}

/// <summary>
/// 把一个官方下载地址展开成「官方 + 镜像」的候选列表。
/// <para>
/// 核心规则：**镜像映射按 URL 类别分别处理**，不能一刀切换掉域名。
/// 资源对象要落到 <c>…/assets</c>，库要落到 <c>…/maven</c>，
/// 元数据与客户端 jar 落到镜像根 —— 混用会大面积 404。
/// </para>
/// </summary>
public sealed class DownloadSourceResolver
{
    /// <summary>内置默认镜像根（BMCLAPI）。</summary>
    public const string DefaultMirrorBase = "https://bmclapi2.bangbang93.com";

    /// <summary>国内 Mod 平台镜像根。</summary>
    public const string DefaultModMirrorBase = "https://mod.mcimirror.top";

    private static readonly string[] OfficialHosts =
    {
        "launchermeta.mojang.com",
        "launcher.mojang.com",
        "piston-meta.mojang.com",
        "piston-data.mojang.com",
        "resources.download.minecraft.net",
        "libraries.minecraft.net",
        "maven.minecraftforge.net",
        "maven.neoforged.net",
    };

    private readonly ILogger _log;

    public DownloadSourceResolver(ILogger log)
    {
        _log = log;
    }

    /// <summary>镜像根地址，可在设置中覆盖。</summary>
    public string MirrorBase { get; set; } = DefaultMirrorBase;

    /// <summary>Mod 平台镜像根地址。</summary>
    public string ModMirrorBase { get; set; } = DefaultModMirrorBase;

    /// <summary>版本清单、版本 JSON 这类元数据使用的源。</summary>
    public DownloadSourcePreference VersionListSource { get; set; } = DownloadSourcePreference.Auto;

    /// <summary>客户端 jar、依赖库、资源对象等游戏文件使用的源。</summary>
    public DownloadSourcePreference FileSource { get; set; } = DownloadSourcePreference.Auto;

    private int _mirrorProbeState; // 0 = 未探测，1 = 可用，2 = 不可用

    /// <summary>Auto 模式下的镜像探测结果；<c>null</c> 表示尚未探测。</summary>
    public bool? MirrorReachable => Volatile.Read(ref _mirrorProbeState) switch
    {
        1 => true,
        2 => false,
        _ => null,
    };

    /// <summary>记录 Auto 模式下的镜像探测结果。</summary>
    public void SetMirrorReachable(bool reachable) =>
        Volatile.Write(ref _mirrorProbeState, reachable ? 1 : 2);

    /// <summary>
    /// 展开候选源。<paramref name="versionId"/> 仅在 <see cref="DownloadUrlKind.Client"/> 时使用，
    /// 用于额外生成镜像的 <c>versions/&lt;id&gt;/&lt;id&gt;.jar</c> 候选。
    /// </summary>
    public IReadOnlyList<DownloadCandidate> Resolve(string url, DownloadUrlKind kind, string? versionId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("下载地址为空", nameof(url));
        }

        var officialUrl = NormalizeOfficial(url);
        var official = new DownloadCandidate(officialUrl, "官方");

        if (kind == DownloadUrlKind.Custom)
        {
            return new[] { official };
        }

        var mirrors = BuildMirrors(officialUrl, kind, versionId);

        // Mod 平台镜像不稳定，官方永远排第一。
        var officialFirst = kind == DownloadUrlKind.ModPlatform || !ShouldPreferMirror(kind);

        var result = new List<DownloadCandidate>(mirrors.Count + 1);
        if (officialFirst)
        {
            result.Add(official);
        }

        foreach (var mirror in mirrors)
        {
            if (!result.Any(c => string.Equals(c.Url, mirror.Url, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(mirror);
            }
        }

        if (!officialFirst)
        {
            result.Add(official);
        }

        return result;
    }

    /// <summary>
    /// 该类别是否优先镜像。元数据跟随「版本列表源」，其余游戏文件跟随「下载源」；
    /// Auto 尚未探测出结果时按镜像可用处理，探测失败后才改为官方优先。
    /// </summary>
    private bool ShouldPreferMirror(DownloadUrlKind kind)
    {
        var preference = kind == DownloadUrlKind.Metadata ? VersionListSource : FileSource;
        return preference switch
        {
            DownloadSourcePreference.Official => false,
            DownloadSourcePreference.Mirror => true,
            _ => MirrorReachable != false,
        };
    }

    private List<DownloadCandidate> BuildMirrors(string url, DownloadUrlKind kind, string? versionId)
    {
        var mirrors = new List<DownloadCandidate>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return mirrors;
        }

        if (kind == DownloadUrlKind.ModPlatform)
        {
            AddModPlatformMirrors(mirrors, url);
            return mirrors;
        }

        var path = uri.AbsolutePath;

        switch (kind)
        {
            case DownloadUrlKind.Asset:
                mirrors.Add(new DownloadCandidate(MirrorBase + "/assets" + path, "BMCLAPI"));
                break;

            case DownloadUrlKind.Library:
            case DownloadUrlKind.ModLoaderLibrary:
            case DownloadUrlKind.ModLoaderInstaller:
                mirrors.Add(new DownloadCandidate(MirrorBase + "/maven" + path, "BMCLAPI(Maven)"));
                mirrors.Add(new DownloadCandidate(MirrorBase + "/libraries" + path, "BMCLAPI(Libraries)"));
                break;

            case DownloadUrlKind.Client:
                if (!string.IsNullOrWhiteSpace(versionId))
                {
                    var safeId = Uri.EscapeDataString(versionId);
                    mirrors.Add(new DownloadCandidate($"{MirrorBase}/versions/{safeId}/{safeId}.jar", "BMCLAPI(版本)"));
                }

                mirrors.Add(new DownloadCandidate(MirrorBase + path, "BMCLAPI"));
                break;

            case DownloadUrlKind.Metadata:
            case DownloadUrlKind.AssetIndex:
                mirrors.Add(new DownloadCandidate(MirrorBase + path, "BMCLAPI"));
                break;
        }

        return mirrors;
    }

    private void AddModPlatformMirrors(List<DownloadCandidate> mirrors, string url)
    {
        // Modrinth / CurseForge 的国内镜像：接口与 CDN 路径不同，必须分别重写。
        var replacements = new (string From, string To)[]
        {
            ("https://api.modrinth.com/", DefaultModMirrorBase + "/modrinth/"),
            ("https://staging-api.modrinth.com/", DefaultModMirrorBase + "/modrinth/"),
            ("https://cdn.modrinth.com/", DefaultModMirrorBase + "/"),
            ("https://api.curseforge.com/", DefaultModMirrorBase + "/curseforge/"),
            ("https://edge.forgecdn.net/", DefaultModMirrorBase + "/"),
            ("https://mediafilez.forgecdn.net/", DefaultModMirrorBase + "/"),
            ("https://media.forgecdn.net/", DefaultModMirrorBase + "/"),
        };

        foreach (var (from, to) in replacements)
        {
            if (url.StartsWith(from, StringComparison.OrdinalIgnoreCase))
            {
                mirrors.Add(new DownloadCandidate(to + url[from.Length..], "MCIMirror"));
            }
        }
    }

    /// <summary>把已知官方地址的 http 升级为 https（旧版本 JSON 里存在明文 http）。</summary>
    private string NormalizeOfficial(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsOfficialHost(uri.Host))
        {
            return url;
        }

        var upgraded = "https://" + url["http://".Length..];
        _log.Trace($"下载地址已升级为 https：{upgraded}");
        return upgraded;
    }

    /// <summary>该地址是否属于已知的官方源（用于判断是否值得生成镜像候选）。</summary>
    public static bool IsOfficialHost(string host) =>
        OfficialHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase)
                               || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
}
