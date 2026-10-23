namespace MCLauncher.Core.Models;

/// <summary>一条崩溃原因与处理建议。</summary>
public sealed class CrashCause
{
    public required string Title { get; init; }

    public required string Advice { get; init; }
}

/// <summary>一次游戏异常退出的分析结果。</summary>
public sealed class CrashReport
{
    public required int ExitCode { get; init; }

    /// <summary>界面标题：有崩溃报告时为「游戏崩溃了」，只有输出时为「游戏异常退出」。</summary>
    public required string Title { get; init; }

    /// <summary>崩溃报告文件路径；JVM 直接退出等场景可能没有。</summary>
    public string? ReportPath { get; init; }

    /// <summary>崩溃报告里的 <c>Description</c>，或游戏输出中的第一条异常声明。</summary>
    public string? Description { get; init; }

    /// <summary>拼装好的完整日志（崩溃报告 + 游戏输出尾部）。</summary>
    public required string LogText { get; init; }

    public required IReadOnlyList<CrashCause> Causes { get; init; }

    public bool HasCrashReport => !string.IsNullOrWhiteSpace(ReportPath);
}

/// <summary>崩溃分析需要的输入。</summary>
public sealed class CrashAnalysisRequest
{
    /// <summary>实例实际使用的游戏目录（隔离实例为 <c>versions/&lt;id&gt;</c>）。</summary>
    public required string GameDirectory { get; init; }

    public required int ExitCode { get; init; }

    /// <summary>本次启动时间，用于区分旧的崩溃报告。</summary>
    public DateTime LaunchedAt { get; init; }

    /// <summary>游戏进程的标准输出 / 错误输出。</summary>
    public string? StandardOutput { get; init; }

    public string? InstanceName { get; init; }
}
