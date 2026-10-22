using System.Text.Json;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Network;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services.Auth;

/// <summary>
/// 管理 authlib-injector 的运行时 jar：首次外置登录启动前自动下载并缓存。
/// 下载源为官方 artifact 与 BMCLAPI 镜像，二者内容一致。
/// </summary>
public sealed class AuthlibInjectorService
{
    private const string LatestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";
    private const string FallbackVersion = "1.2.8";
    private const int FallbackBuild = 56;
    private const long MinimumJarSize = 100_000;

    private readonly HttpDownloader _downloader;
    private readonly ILogger _log;

    public AuthlibInjectorService(HttpDownloader downloader, ILogger log)
    {
        _downloader = downloader;
        _log = log;
    }

    /// <summary>确保 jar 存在并返回绝对路径。</summary>
    public async Task<string> EnsureJarAsync(string cacheDirectory, CancellationToken ct = default)
    {
        var directory = Path.Combine(cacheDirectory, "authlib-injector");
        PathUtil.EnsureDirectory(directory);

        var cached = FindCachedJar(directory);
        if (cached is not null)
        {
            return cached;
        }

        var artifact = await ResolveLatestAsync(ct).ConfigureAwait(false);
        var fileName = $"authlib-injector-{artifact.Version}.jar";
        var target = Path.Combine(directory, fileName);

        if (File.Exists(target) && new FileInfo(target).Length < MinimumJarSize)
        {
            // 上次没下完的残片，删掉重来。
            File.Delete(target);
        }

        if (File.Exists(target))
        {
            return target;
        }

        var candidates = new List<DownloadCandidate>
        {
            new(artifact.DownloadUrl, "官方"),
            new(
                $"https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/{artifact.Build}/{fileName}",
                "BMCLAPI"),
        };

        var outcome = await _downloader
            .DownloadAsync(candidates, target, FileChecker.ExistenceOnly, null, ct)
            .ConfigureAwait(false);

        if (!outcome.Success || !File.Exists(target) || new FileInfo(target).Length < MinimumJarSize)
        {
            throw new AuthlibAuthException(
                $"下载 authlib-injector 失败：{outcome.Error ?? "文件不完整"}。请检查网络后重试");
        }

        _log.Info($"authlib-injector 已就绪：{target}");
        return target;
    }

    private static string? FindCachedJar(string directory)
    {
        var jar = Directory
            .GetFiles(directory, "authlib-injector-*.jar")
            .Where(path => new FileInfo(path).Length >= MinimumJarSize)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return jar;
    }

    private async Task<(string Version, int Build, string DownloadUrl)> ResolveLatestAsync(CancellationToken ct)
    {
        try
        {
            var json = await _downloader
                .GetStringAsync(new[] { new DownloadCandidate(LatestUrl, "官方") }, ct)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            var url = root.TryGetProperty("download_url", out var u) ? u.GetString() : null;
            var build = root.TryGetProperty("build_number", out var b) ? b.GetInt32() : FallbackBuild;

            if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(url))
            {
                return (version!, build, url!);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("获取 authlib-injector 最新版本失败，使用内置版本", ex);
        }

        return (
            FallbackVersion,
            FallbackBuild,
            $"https://authlib-injector.yushi.moe/artifact/{FallbackBuild}/authlib-injector-{FallbackVersion}.jar");
    }
}
