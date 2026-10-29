using System.Globalization;
using System.Net;
using System.Text.Json;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Network;

namespace MCLauncher.Core.Services;

/// <summary>启动器公告。</summary>
public sealed class LauncherNotice
{
    /// <summary>稳定标识，用于记住「已关闭」。</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Message { get; init; }

    public string? Url { get; init; }
}

/// <summary>GitHub Releases 里的最新版本。</summary>
public sealed class LauncherUpdate
{
    public required string Version { get; init; }

    public string? Title { get; init; }

    public string? Notes { get; init; }

    public string? Url { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>
/// 启动器自身的公告与版本检查。数据放在项目仓库里：
/// 公告读 <c>notice.json</c>（官方 raw + jsDelivr 镜像），更新读 GitHub Releases API。
/// 所有失败都只记日志并返回 null，启动流程不能因为这两个可选项而中断。
/// </summary>
public sealed class LauncherInfoService
{
    private const string Repository = "HotWaterFlask/MCLauncher-Reborn";

    private const string NoticeUrl =
        "https://raw.githubusercontent.com/" + Repository + "/main/notice.json";

    private const string NoticeMirrorUrl =
        "https://cdn.jsdelivr.net/gh/" + Repository + "@main/notice.json";

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/" + Repository + "/releases/latest";

    private readonly HttpDownloader _downloader;
    private readonly ILogger _log;

    public LauncherInfoService(HttpDownloader downloader, ILogger log)
    {
        _downloader = downloader;
        _log = log;
    }

    /// <summary>获取公告；没有公告或获取失败时返回 null。</summary>
    public async Task<LauncherNotice?> FetchNoticeAsync(CancellationToken ct = default)
    {
        var candidates = new[]
        {
            new DownloadCandidate(NoticeUrl, "GitHub"),
            new DownloadCandidate(NoticeMirrorUrl, "jsDelivr"),
        };

        try
        {
            var json = await _downloader.GetStringAsync(candidates, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
            {
                return null;
            }

            var title = GetString(root, "title");
            var id = GetString(root, "id") ?? title;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new LauncherNotice
            {
                Id = id.Trim(),
                Title = title.Trim(),
                Message = GetString(root, "message"),
                Url = GetString(root, "url"),
            };
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            _log.Info("仓库暂无公告（notice.json 不存在或尚未发布）");
            return null;
        }
        catch (Exception ex)
        {
            _log.Warn("获取启动器公告失败", ex);
            return null;
        }
    }

    /// <summary>检查最新版本；没有发布或接口失败时返回 null。</summary>
    public async Task<LauncherUpdate?> CheckUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _downloader
                .GetStringAsync(new[] { new DownloadCandidate(LatestReleaseUrl, "GitHub") }, ct)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var version = GetString(root, "tag_name");
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            DateTimeOffset? publishedAt = null;
            if (GetString(root, "published_at") is { } text
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                publishedAt = parsed;
            }

            return new LauncherUpdate
            {
                Version = version.Trim(),
                Title = GetString(root, "name"),
                Notes = GetString(root, "body"),
                Url = GetString(root, "html_url"),
                PublishedAt = publishedAt,
            };
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            _log.Info("仓库暂无已发布版本，跳过更新检查");
            return null;
        }
        catch (Exception ex)
        {
            _log.Warn("检查启动器更新失败", ex);
            return null;
        }
    }

    /// <summary>
    /// 比较版本号（允许前缀 <c>v</c> 与 <c>-beta</c> 之类后缀）。只在候选确实更高时返回 true。
    /// </summary>
    public static bool IsNewerVersion(string? candidate, string current)
    {
        var left = ParseVersion(candidate);
        var right = ParseVersion(current);
        if (left is null || right is null)
        {
            return false;
        }

        var length = Math.Max(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            if (a != b)
            {
                return a > b;
            }
        }

        return false;
    }

    private static int[]? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().TrimStart('v', 'V');
        var cut = text.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0)
        {
            text = text[..cut];
        }

        var parts = text.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return numbers.Length == 0 ? null : numbers;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>GitHub 的 404 表示「还没有这个文件/版本」，属于正常状态而不是错误。</summary>
    private static bool IsNotFound(Exception exception) =>
        exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
        || exception.InnerException is HttpRequestException { StatusCode: HttpStatusCode.NotFound };
}
