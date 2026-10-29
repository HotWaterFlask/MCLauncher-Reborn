using System.Text;
using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Logging;

/// <summary>
/// 把日志追加到 logs\launcher-yyyyMMdd.log，并在超过阈值时做一次简单轮转。
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>普通日志攒够这么多行再 Flush，避免每行一次磁盘写。</summary>
    private const int FlushThreshold = 32;

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _currentFile;
    private int _pendingLines;

    /// <param name="minimumLevel">
    /// 最低记录级别，默认 Info：Trace 只用于开发期排查，不应长期落在用户日志里。
    /// </param>
    public FileLogSink(string directory, LogLevel minimumLevel = LogLevel.Info)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(_directory);
    }

    public void Write(LogLevel level, string message)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine(message);
                _pendingLines++;

                // 告警与错误立即落盘，保证崩溃前的最后几条不会丢；普通日志按批 Flush。
                if (level >= LogLevel.Warn || _pendingLines >= FlushThreshold)
                {
                    FlushLocked();
                }

                if (_writer!.BaseStream.Length > MaxBytes)
                {
                    FlushLocked();
                    _writer.Dispose();
                    _writer = null;
                    Rotate();
                }
            }
            catch
            {
                // 落盘失败不影响主流程。
            }
        }
    }

    private void FlushLocked()
    {
        _pendingLines = 0;
        _writer?.Flush();
    }

    private void EnsureWriter()
    {
        var file = Path.Combine(_directory, $"launcher-{DateTime.Now:yyyyMMdd}.log");
        if (_writer is not null && string.Equals(_currentFile, file, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _writer?.Dispose();
        _currentFile = file;
        _pendingLines = 0;
        _writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
    }

    private void Rotate()
    {
        try
        {
            var file = _currentFile;
            if (file is null || !File.Exists(file))
            {
                return;
            }

            var backup = Path.Combine(_directory, $"launcher-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}.log");
            File.Move(file, backup);
        }
        catch
        {
            // 轮转失败就继续追加。
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                FlushLocked();
            }
            catch
            {
                // 退出阶段写失败没有补救手段。
            }

            _writer?.Dispose();
            _writer = null;
        }
    }
}
