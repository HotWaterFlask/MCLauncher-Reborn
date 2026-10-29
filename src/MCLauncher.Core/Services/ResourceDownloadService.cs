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

    public required ResourceFile File { get; init; }

    /// <summary>实例目录（资源会落到对应的 mods / resourcepacks / shaderpacks / saves 下）。</summary>
    public required string InstanceDirectory { get; init; }

    /// <summary>更新时传入原文件路径；为空表示新增下载。</summary>
    public string? ReplacePath { get; init; }

    /// <summary>自定义文件名（安装到实例时可由用户改名）；为空时使用文件原名。</summary>
    public string? TargetFileName { get; init; }
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

    /// <summary>把文件原样下载到用户选择的目录（不安装进实例）。</summary>
    public async Task<ResourceDownloadResult> DownloadToFolderAsync(
        ResourceFile file,
        string directory,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
        {
            return ResourceDownloadResult.Fail("该项目没有开放第三方下载，请到资源站手动下载。");
        }

        if (!Directory.Exists(directory))
        {
            return ResourceDownloadResult.Fail("下载目录不存在。");
        }

        var fileName = Path.GetFileName(file.FileName);
        if (!PathUtil.IsValidFileName(fileName))
        {
            return ResourceDownloadResult.Fail($"文件名不合法：{file.FileName}");
        }

        var target = UniqueTarget(directory, fileName);
        var result = await DownloadFileCoreAsync(file, target, target, progress, ct).ConfigureAwait(false);

        if (result.Success)
        {
            _log.Info($"已下载文件：{target}");
        }

        return result;
    }

    private async Task<ResourceDownloadResult> DownloadFileAsync(
        ResourceDownloadRequest request,
        string directory,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        var fileName = string.IsNullOrWhiteSpace(request.TargetFileName)
            ? Path.GetFileName(request.File.FileName)
            : Path.GetFileName(request.TargetFileName.Trim());

        if (!PathUtil.IsValidFileName(fileName))
        {
            return ResourceDownloadResult.Fail($"文件名不合法：{fileName}");
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
        else if (!string.IsNullOrWhiteSpace(request.TargetFileName))
        {
            target = Path.Combine(directory, fileName);
            if (File.Exists(target)
                || Directory.Exists(target)
                || File.Exists(target + ResourceRepository.DisabledSuffix))
            {
                return ResourceDownloadResult.Fail($"已存在同名文件：{fileName}");
            }

            temp = target;
        }
        else
        {
            target = UniqueTarget(directory, fileName);
            temp = target;
        }

        var result = await DownloadFileCoreAsync(request.File, temp, target, progress, ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            // 临时文件与 .part 在失败后都没有价值，留着只会越积越多。
            if (!PathUtil.IsSamePath(temp, target))
            {
                TryDeleteFile(temp);
                TryDeleteFile(temp + ".part");
            }

            return result;
        }

        if (request.ReplacePath is { Length: > 0 } replacedPath
            && !PathUtil.IsSamePath(replacedPath, target))
        {
            TryDeleteFile(replacedPath);
        }

        _log.Info($"已下载 {ResourceRepository.DisplayName(request.Kind)}：{fileName}");
        return result;
    }

    private async Task<ResourceDownloadResult> DownloadFileCoreAsync(
        ResourceFile file,
        string tempPath,
        string targetPath,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(file.FileName);

        var requests = new[]
        {
            new DownloadRequest
            {
                Name = fileName,
                Candidates = _resolver.Resolve(file.DownloadUrl!, DownloadUrlKind.ModPlatform),
                TargetPath = tempPath,
                Checker = FileChecker.Of(file.Sha1, file.FileLength),
            },
        };

        var batch = new DownloadBatch(_downloader, _log);
        var progressAdapter = progress is null
            ? null
            : new InlineProgress<DownloadBatchProgress>(value =>
                progress.Report(ToInstallProgress("下载 " + fileName, value)));

        DownloadBatchResult batchResult;
        try
        {
            batchResult = await batch.RunAsync(requests, 1, progressAdapter, ct).ConfigureAwait(false);
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

        if (!batchResult.Success)
        {
            var error = batchResult.Failures.Count > 0
                ? batchResult.Failures[0].Error
                : "没有可用的下载源";
            return ResourceDownloadResult.Fail("下载失败：" + error);
        }

        try
        {
            if (!PathUtil.IsSamePath(tempPath, targetPath))
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"保存资源文件失败：{targetPath}", ex);
            return ResourceDownloadResult.Fail("文件已下载，但保存失败：" + ex.Message);
        }

        return ResourceDownloadResult.Ok(targetPath);
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
            var requests = new[]
            {
                new DownloadRequest
                {
                    Name = request.File.FileName,
                    Candidates = _resolver.Resolve(request.File.DownloadUrl!, DownloadUrlKind.ModPlatform),
                    TargetPath = tempZip,
                    Checker = FileChecker.Of(request.File.Sha1, request.File.FileLength),
                },
            };

            var batch = new DownloadBatch(_downloader, _log);
            var progressAdapter = progress is null
                ? null
                : new InlineProgress<DownloadBatchProgress>(value =>
                    progress.Report(ToInstallProgress("下载 " + request.File.FileName, value)));

            var batchResult = await batch
                .RunAsync(requests, 1, progressAdapter, ct)
                .ConfigureAwait(false);

            if (!batchResult.Success)
            {
                var error = batchResult.Failures.Count > 0
                    ? batchResult.Failures[0].Error
                    : "没有可用的下载源";
                return ResourceDownloadResult.Fail("下载失败：" + error);
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

    /// <summary>把批量下载进度转成界面统一的安装进度（单文件下载也走同一模型）。</summary>
    private static InstallProgress ToInstallProgress(string stage, DownloadBatchProgress progress) => new()
    {
        Stage = stage,
        Completed = progress.Completed + progress.Skipped,
        Total = progress.Total,
        DownloadedBytes = progress.DownloadedBytes,
        TotalBytes = progress.TotalBytes,
        Current = progress.Current,
        ActiveFiles = progress.ActiveFiles,
        BytesPerSecond = progress.BytesPerSecond,
    };

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
