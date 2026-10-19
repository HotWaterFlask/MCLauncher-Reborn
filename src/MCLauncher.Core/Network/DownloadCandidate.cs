namespace MCLauncher.Core.Network;

/// <summary>
/// 一个下载候选源：具体 URL + 来源名。
/// 同一份文件往往有多个候选（官方 + 镜像），下载器按顺序轮询，全部失败才判定失败。
/// </summary>
public sealed class DownloadCandidate
{
    public DownloadCandidate(string url, string sourceName)
    {
        Url = url;
        SourceName = sourceName;
    }

    public string Url { get; }

    /// <summary>用于日志与界面展示的来源名，如「官方」「BMCLAPI」。</summary>
    public string SourceName { get; }

    public override string ToString() => $"{SourceName} {Url}";
}
