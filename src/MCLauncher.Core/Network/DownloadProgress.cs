namespace MCLauncher.Core.Network;

/// <summary>单个文件的下载进度快照。</summary>
public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>总量未知时为 null（服务器未给出 Content-Length）。</summary>
    public double? Fraction => TotalBytes > 0 ? (double)BytesReceived / TotalBytes : null;

    public static DownloadProgress Unknown(long bytesReceived) => new(bytesReceived, -1);
}

/// <summary>一批下载任务的聚合进度快照。</summary>
public sealed class DownloadBatchProgress
{
    public int Total { get; init; }

    public int Completed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public long TotalBytes { get; init; }

    public long DownloadedBytes { get; init; }

    /// <summary>最近一条状态文字（当前下载的文件名等）。</summary>
    public string? Current { get; init; }

    public double Fraction => Total > 0 ? (double)(Completed + Skipped) / Total : 0d;
}
