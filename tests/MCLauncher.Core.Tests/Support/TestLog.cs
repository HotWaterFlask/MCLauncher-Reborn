using MCLauncher.Core.Abstractions;
using Xunit.Abstractions;

namespace MCLauncher.Core.Tests.Support;

/// <summary>把日志同时收集到内存，并转发到测试输出。</summary>
internal sealed class TestLog : ILogger
{
    private readonly List<string> _lines = new();
    private readonly ITestOutputHelper? _output;

    public TestLog(ITestOutputHelper? output = null) => _output = output;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToArray();
            }
        }
    }

    public bool Contains(string fragment) =>
        Lines.Any(line => line.Contains(fragment, StringComparison.Ordinal));

    public void Trace(string message) => Write("TRC", message);

    public void Info(string message) => Write("INF", message);

    public void Warn(string message, Exception? exception = null) => Write("WRN", message + Format(exception));

    public void Error(string message, Exception? exception = null) => Write("ERR", message + Format(exception));

    public void AttachSink(ILogSink sink)
    {
    }

    private static string Format(Exception? exception) =>
        exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";

    private void Write(string level, string message)
    {
        lock (_lines)
        {
            _lines.Add($"[{level}] {message}");
        }

        try
        {
            _output?.WriteLine($"[{level}] {message}");
        }
        catch
        {
            // 测试输出在部分运行器下不可用，忽略。
        }
    }
}
