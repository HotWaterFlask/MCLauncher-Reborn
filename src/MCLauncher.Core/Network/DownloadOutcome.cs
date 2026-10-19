namespace MCLauncher.Core.Network;

/// <summary>一次下载尝试的结果。</summary>
public readonly record struct DownloadOutcome(
    bool Success,
    bool Skipped,
    int Attempts,
    string? SourceName,
    string? Error)
{
    public static DownloadOutcome AlreadyPresent(int attempts = 0) =>
        new(true, true, attempts, null, null);

    public static DownloadOutcome Failed(int attempts, string? error) =>
        new(false, false, attempts, null, error);
}
