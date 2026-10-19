using System.Collections.Concurrent;
using System.Diagnostics;
using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Network;

/// <summary>一条批量下载请求。</summary>
public sealed class DownloadRequest
{
    /// <summary>展示名（如 <c>1.20.4.json</c>），用于日志与进度提示。</summary>
    public required string Name { get; init; }

    /// <summary>候选下载源，按顺序轮询。</summary>
    public required IReadOnlyList<DownloadCandidate> Candidates { get; init; }

    /// <summary>落盘目标路径。</summary>
    public required string TargetPath { get; init; }

    /// <summary>校验规则。</summary>
    public FileChecker Checker { get; init; } = FileChecker.ExistenceOnly;

    /// <summary>可选文件：失败只记录，不让整批失败（例如某个可有可无的库）。</summary>
    public bool Optional { get; init; }
}

/// <summary>一条失败记录。</summary>
public readonly record struct DownloadFailure(string Name, string TargetPath, string Error);

/// <summary>批量下载汇总结果。</summary>
public sealed class DownloadBatchResult
{
    public int Total { get; init; }

    public int Completed { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<DownloadFailure> Failures { get; init; } = Array.Empty<DownloadFailure>();

    /// <summary>必需文件全部成功即视为成功（可选文件的失败不算）。</summary>
    public bool Success => Failures.Count == 0;
}

/// <summary>
/// 批量下载调度：给一批文件加并发上限，并把每条文件的进度聚合成一个整体进度上报。
/// 真正的限流（主机并发桶 / 请求间隔 / 断点续传）由 <see cref="HttpDownloader"/> 负责。
/// </summary>
public sealed class DownloadBatch
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private readonly HttpDownloader _downloader;
    private readonly ILogger _log;
    private long _lastReportTicks;

    public DownloadBatch(HttpDownloader downloader, ILogger log)
    {
        _downloader = downloader;
        _log = log;
    }

    public async Task<DownloadBatchResult> RunAsync(
        IReadOnlyList<DownloadRequest> requests,
        int maxParallel = 8,
        IProgress<DownloadBatchProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (requests.Count == 0)
        {
            return new DownloadBatchResult();
        }

        var total = requests.Count;
        var totalBytes = requests.Sum(r => r.Checker.Size > 0 ? r.Checker.Size : 0);

        var completed = 0;
        var skipped = 0;
        var failed = 0;
        long downloadedBytes = 0;
        var failures = new ConcurrentQueue<DownloadFailure>();

        _lastReportTicks = 0;
        Report(progress, total, 0, 0, 0, totalBytes, 0, "开始下载", force: true);

        _log.Info($"开始批量下载：共 {total} 个文件，并发 {maxParallel}");

        await Parallel.ForEachAsync(
            requests,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallel), CancellationToken = ct },
            async (request, token) =>
            {
                long localReported = 0;

                var fileProgress = new InlineProgress<DownloadProgress>(p =>
                {
                    var delta = p.BytesReceived - localReported;
                    if (delta != 0)
                    {
                        localReported = p.BytesReceived;
                        Interlocked.Add(ref downloadedBytes, delta);
                    }

                    Report(progress, total, Volatile.Read(ref completed), Volatile.Read(ref skipped), Volatile.Read(ref failed),
                        totalBytes, Interlocked.Read(ref downloadedBytes), request.Name, force: false);
                });

                DownloadOutcome outcome;
                try
                {
                    outcome = await _downloader
                        .DownloadAsync(request.Candidates, request.TargetPath, request.Checker, fileProgress, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    outcome = DownloadOutcome.Failed(0, ex.Message);
                }

                if (outcome.Success)
                {
                    if (outcome.Skipped)
                    {
                        // 已存在的文件没有进度回调，直接把它的体积计入总量。
                        if (request.Checker.Size > 0)
                        {
                            Interlocked.Add(ref downloadedBytes, request.Checker.Size);
                        }

                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        Interlocked.Increment(ref completed);
                    }
                }
                else if (request.Optional)
                {
                    _log.Warn($"可选文件下载失败（已忽略）：{request.Name} —— {outcome.Error}");
                    Interlocked.Increment(ref skipped);
                }
                else
                {
                    failures.Enqueue(new DownloadFailure(request.Name, request.TargetPath, outcome.Error ?? "未知错误"));
                    Interlocked.Increment(ref failed);
                }

                Report(progress, total, Volatile.Read(ref completed), Volatile.Read(ref skipped), Volatile.Read(ref failed),
                    totalBytes, Interlocked.Read(ref downloadedBytes), request.Name, force: true);
            }).ConfigureAwait(false);

        var result = new DownloadBatchResult
        {
            Total = total,
            Completed = completed,
            Skipped = skipped,
            Failed = failed,
            Failures = failures.ToArray(),
        };

        if (result.Success)
        {
            _log.Info($"批量下载完成：共 {total}，新下载 {completed}，已存在 {skipped}");
        }
        else
        {
            _log.Warn($"批量下载存在失败：共 {total}，失败 {failed}");
            foreach (var failure in result.Failures)
            {
                _log.Warn($"  失败文件：{failure.Name} -> {failure.TargetPath}（{failure.Error}）");
            }
        }

        return result;
    }

    private void Report(
        IProgress<DownloadBatchProgress>? progress,
        int total,
        int completed,
        int skipped,
        int failed,
        long totalBytes,
        long downloadedBytes,
        string? current,
        bool force)
    {
        if (progress is null)
        {
            return;
        }

        if (!force)
        {
            var last = Volatile.Read(ref _lastReportTicks);
            if (last != 0 && Stopwatch.GetElapsedTime(last) < ReportInterval)
            {
                return;
            }
        }

        Volatile.Write(ref _lastReportTicks, Stopwatch.GetTimestamp());

        progress.Report(new DownloadBatchProgress
        {
            Total = total,
            Completed = completed,
            Skipped = skipped,
            Failed = failed,
            TotalBytes = totalBytes,
            DownloadedBytes = downloadedBytes,
            Current = current,
        });
    }
}
