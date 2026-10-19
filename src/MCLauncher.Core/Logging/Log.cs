using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Logging;

/// <summary>默认日志实现：线程安全，可挂多个 sink。</summary>
public sealed class Log : ILogger
{
    private readonly List<ILogSink> _sinks = new();
    private readonly object _gate = new();

    /// <summary>全局默认实例。启动时把文件 sink 挂上即可。</summary>
    public static Log Default { get; } = new();

    public void AttachSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_gate)
        {
            _sinks.Add(sink);
        }
    }

    public void Trace(string message) => Write(LogLevel.Trace, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message, Exception? exception = null) =>
        Write(LogLevel.Warn, Compose(message, exception));

    public void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, Compose(message, exception));

    private static string Compose(string message, Exception? exception) =>
        exception is null ? message : $"{message}{Environment.NewLine}{exception}";

    private void Write(LogLevel level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Abbrev(level)}] {message}";

        ILogSink[] targets;
        lock (_gate)
        {
            targets = _sinks.ToArray();
        }

        foreach (var sink in targets)
        {
            try
            {
                sink.Write(level, line);
            }
            catch
            {
                // 日志自身不能把程序搞崩：某个 sink 失败就跳过它。
            }
        }
    }

    private static string Abbrev(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "???",
    };
}
