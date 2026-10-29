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

    /// <summary>latest.json 不可达时使用固定版本的 SHA-256，避免在没有哈希的情况下下载 jar。</summary>
    private const string FallbackSha256 =
        "9c7f4343e6c82034958ffb48c14a2cb0c85928be7283103ce17da00c6d5a7b10";

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

        if (File.Exists(target) && Matches(target, artifact.Sha256))
        {
            WriteSidecar(target, artifact.Sha256 ?? ReadSidecar(target));
            return target;
        }

        if (string.IsNullOrWhiteSpace(artifact.Sha256))
        {
            throw new AuthlibAuthException(
                "authlib-injector 元数据没有提供 SHA-256 校验值，已拒绝下载；请稍后重试或手动把 jar 放到缓存目录。");
        }

        if (File.Exists(target))
        {
            // 上次没下完或被篡改的残片，删掉重来。
            TryDelete(target);
        }

        var candidates = new List<DownloadCandidate>
        {
            new(artifact.DownloadUrl, "官方"),
            new(
                $"https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/{artifact.Build}/{fileName}",
                "BMCLAPI"),
        };

        var batch = new DownloadBatch(_downloader, _log);
        var result = await batch
            .RunAsync(
                new[]
                {
                    new DownloadRequest
                    {
                        Name = fileName,
                        Candidates = candidates,
                        TargetPath = target,
                        Checker = FileChecker.ExistenceOnly,
                    },
                },
                1,
                null,
                ct)
            .ConfigureAwait(false);

        if (!result.Success || !File.Exists(target) || !Matches(target, artifact.Sha256))
        {
            var error = result.Failures.Count > 0
                ? result.Failures[0].Error
                : "文件不完整或 SHA-256 校验失败";
            TryDelete(target);
            throw new AuthlibAuthException(
                $"下载 authlib-injector 失败：{error}。请检查网络后重试");
        }

        WriteSidecar(target, artifact.Sha256);
        _log.Info($"authlib-injector 已就绪：{target}");
        return target;
    }

    /// <summary>校验缓存 jar：优先用元数据里的 SHA-256，其次用同目录的 <c>.sha256</c> 旁路文件。</summary>
    private static bool Matches(string path, string? sha256)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MinimumJarSize)
        {
            return false;
        }

        var expected = string.IsNullOrWhiteSpace(sha256) ? ReadSidecar(path) : sha256;
        return !string.IsNullOrWhiteSpace(expected) && HashUtil.VerifySha256(path, expected);
    }

    private static string? ReadSidecar(string path)
    {
        try
        {
            var sidecar = path + ".sha256";
            return File.Exists(sidecar) ? File.ReadAllText(sidecar).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteSidecar(string path, string? sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return;
        }

        try
        {
            File.WriteAllText(path + ".sha256", sha256);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 旁路文件只是缓存加速，写失败不影响本次启动。
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? FindCachedJar(string directory)
    {
        var jar = Directory
            .GetFiles(directory, "authlib-injector-*.jar")
            .Where(path => Matches(path, null))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return jar;
    }

    private async Task<(string Version, int Build, string DownloadUrl, string? Sha256)> ResolveLatestAsync(CancellationToken ct)
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
            var sha256 = root.TryGetProperty("checksums", out var checksums)
                         && checksums.TryGetProperty("sha256", out var s)
                ? s.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(url))
            {
                return (version!, build, url!, string.IsNullOrWhiteSpace(sha256) ? null : sha256);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("获取 authlib-injector 最新版本失败，使用内置版本", ex);
        }

        return (
            FallbackVersion,
            FallbackBuild,
            $"https://authlib-injector.yushi.moe/artifact/{FallbackBuild}/authlib-injector-{FallbackVersion}.jar",
            FallbackSha256);
    }
}
