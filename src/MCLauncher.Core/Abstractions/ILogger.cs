namespace MCLauncher.Core.Abstractions;

/// <summary>日志级别。</summary>
public enum LogLevel
{
    Trace,
    Info,
    Warn,
    Error,
}

/// <summary>日志输出目标（文件、内存缓冲、界面等）。</summary>
public interface ILogSink
{
    void Write(LogLevel level, string message);
}

/// <summary>启动器日志接口。Core 内所有模块统一通过它输出。</summary>
public interface ILogger
{
    void Trace(string message);

    void Info(string message);

    void Warn(string message, Exception? exception = null);

    void Error(string message, Exception? exception = null);

    /// <summary>挂载一个输出目标，可挂载多个。</summary>
    void AttachSink(ILogSink sink);
}
