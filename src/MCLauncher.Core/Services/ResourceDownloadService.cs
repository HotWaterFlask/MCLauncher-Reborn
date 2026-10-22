using System.IO;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>一次资源下载请求。</summary>
public sealed class ResourceDownloadRequest
{
    public required ResourceKind Kind { get; init; }

    public required CurseForgeFile File { get; init; }

    /// <summary>实例目录（资源会落到对应的 mods / resourcepacks / shaderpacks / saves 下）。</summary>
    public required string InstanceDirectory { get; init; }

    /// <summary>更新时传入原文件路径；为空表示新增下载。</summary>
    public string? ReplacePath { get; init; }
}

public sealed class ResourceDownloadResult
{
    public bool Success { get; init; }

    public string? InstalledPath { get; init; }

    public string? Error { get; init; }

    public static ResourceDownloadResult Ok(string path) => new() { Success = true, InstalledPath = path };

    public static ResourceDownloadResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// 把 CurseForge 文件下载到实例目录。
/// 文件类资源直接落到对应目录；世界压缩包先解压到临时目录再移动成 saves/&lt;存档&gt;。
/// </summary>
public sealed class ResourceDownloadService
{
    private readonly HttpDownloader _downloader;
    private readonly DownloadSourceResolver _resolver;
    private readonly ILogger _log;

    public ResourceDownloadService(HttpDownloader downloader, DownloadSourceResolver resolver, ILogger log)
    {
        _downloader = downloader;
        _resolver = resolver;
        _log = log;
    }

    public async Task<ResourceDownloadResult> DownloadAsync(
        ResourceDownloadRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.File.DownloadUrl))
        {
            return ResourceDownloadResult.Fail("该项目没有开放第三方下载，请到 CurseForge 网站手动下载。");
        }

        var directory = ResourceRepository.GetDirectory(request.InstanceDirectory, request.Kind);
        PathUtil.EnsureDirectory(directory);

        return request.Kind == ResourceKind.World
            ? await DownloadWorldAsync(request, directory, progress, ct).ConfigureAwait(false)
            : await DownloadFileAsync(request, directory, progress, ct).ConfigureAwait(false);
    }

    private async Task<ResourceDownloadResult> DownloadFileAsync(
        ResourceDownloadRequest request,
        string directory,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(request.File.FileName);
        if (!PathUtil.IsValidFileName(fileName))
        {
            return ResourceDownloadResult.Fail($"文件名不合法：{request.File.FileName}");
        }

        string target;
        string temp;

        if (request.ReplacePath is { Length: > 0 } replacePath)
        {
            if (Directory.Exists(replacePath))
            {
                return ResourceDownloadResult.Fail("当前资源是目录形式，无法自动替换。");
            }

            var disabled = replacePath.EndsWith(ResourceRepository.DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            var targetName = disabled ? fileName + ResourceRepository.DisabledSuffix : fileName;

            target = Path.Combine(directory, targetName);
            temp = target + ".download";
        }
        else
        {
            target = UniqueTarget(directory, fileName);
            temp = target;
        }

        var progressAdapter = progress is null
            ? null
            : new InlineProgress<DownloadProgress>(value =>
            {
                progress.Report(new InstallProgress
                {
                    Stage = "下载 " + fileName,
                    Completed = (int)Math.Round((value.Fraction ?? 0) * 100),
                    Total = 100,
                    DownloadedBytes = value.BytesReceived,
                    TotalBytes = value.TotalBytes,
                    Current = fileName,
                });
            });

        DownloadOutcome outcome;
        try
        {
            outcome = await _downloader.DownloadAsync(
                    _resolver.Resolve(request.File.DownloadUrl!, DownloadUrlKind.ModPlatform),
                    temp,
                    FileChecker.Of(request.File.Sha1, request.File.FileLength),
                    progressAdapter,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log.Warn($"下载资源失败：{fileName}", ex);
            return ResourceDownloadResult.Fail("下载失败：" + ex.Message);
        }

        if (!outcome.Success)
        {
            return ResourceDownloadResult.Fail("下载失败：" + (outcome.Error ?? "没有可用的下载源"));
        }

        try
        {
            if (!PathUtil.IsSamePath(temp, target))
            {
                File.Move(temp, target, overwrite: true);

                if (File.Exists(request.ReplacePath) && !PathUtil.IsSamePath(request.ReplacePath, target))
                {
                    File.Delete(request.ReplacePath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"替换资源文件失败：{target}", ex);
            return ResourceDownloadResult.Fail("文件已下载，但替换失败：" + ex.Message);
        }

        _log.Info($"已下载 {ResourceRepository.DisplayName(request.Kind)}：{fileName}");
        return ResourceDownloadResult.Ok(target);
    }

    private async Task<ResourceDownloadResult> DownloadWorldAsync(
        ResourceDownloadRequest request,
        string savesDirectory,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"mclauncher-world-{Guid.NewGuid():N}.zip");

        try
        {
            var progressAdapter = progress is null
                ? null
                : new InlineProgress<DownloadProgress>(value =>
                {
                    progress.Report(new InstallProgress
                    {
                        Stage = "下载 " + request.File.FileName,
                        Completed = (int)Math.Round((value.Fraction ?? 0) * 100),
                        Total = 100,
                        DownloadedBytes = value.BytesReceived,
                        TotalBytes = value.TotalBytes,
                        Current = request.File.FileName,
                    });
                });

            var outcome = await _downloader.DownloadAsync(
                    _resolver.Resolve(request.File.DownloadUrl!, DownloadUrlKind.ModPlatform),
                    tempZip,
                    FileChecker.Of(request.File.Sha1, request.File.FileLength),
                    progressAdapter,
                    ct)
                .ConfigureAwait(false);

            if (!outcome.Success)
            {
                return ResourceDownloadResult.Fail("下载失败：" + (outcome.Error ?? "没有可用的下载源"));
            }

            progress?.Report(new InstallProgress { Stage = "解压存档", Completed = 0, Total = 1 });

            var extractDirectory = Path.Combine(Path.GetTempPath(), $"mclauncher-world-{Guid.NewGuid():N}");
            PathUtil.EnsureDirectory(extractDirectory);
            try
            {
                ArchiveUtil.ExtractZip(tempZip, extractDirectory);

                var worldRoot = FindWorldRoot(extractDirectory)
                                ?? extractDirectory;
                var baseName = PathUtil.MakeSafeFileName(
                    Path.GetFileNameWithoutExtension(request.File.FileName));
                var target = UniqueTarget(savesDirectory, baseName);

                Directory.Move(worldRoot, target);

                progress?.Report(new InstallProgress { Stage = "解压存档", Completed = 1, Total = 1 });
                _log.Info($"已下载世界存档：{request.File.FileName} → {target}");
                return ResourceDownloadResult.Ok(target);
            }
            finally
            {
                TryDeleteDirectory(extractDirectory);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log.Warn($"下载世界存档失败：{request.File.FileName}", ex);
            return ResourceDownloadResult.Fail("下载失败：" + ex.Message);
        }
        finally
        {
            TryDeleteFile(tempZip);
        }
    }

    /// <summary>
    /// 在解压目录里找真正的存档根：优先根目录的 level.dat，其次一层子目录里的 level.dat。
    /// </summary>
    private static string? FindWorldRoot(string directory)
    {
        if (File.Exists(Path.Combine(directory, "level.dat")))
        {
            return directory;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (File.Exists(Path.Combine(child, "level.dat")))
            {
                return child;
            }

            foreach (var grandChild in Directory.EnumerateDirectories(child))
            {
                if (File.Exists(Path.Combine(grandChild, "level.dat")))
                {
                    return grandChild;
                }
            }
        }

        return null;
    }

    private static string UniqueTarget(string directory, string fileName)
    {
        var target = Path.Combine(directory, fileName);
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return target;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; index < 1000; index++)
        {
            target = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                return target;
            }
        }

        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
