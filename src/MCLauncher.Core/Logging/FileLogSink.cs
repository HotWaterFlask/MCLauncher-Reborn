using System.Text;
using MCLauncher.Core.Abstractions;

namespace MCLauncher.Core.Logging;

/// <summary>
/// 把日志追加到 logs\launcher-yyyyMMdd.log，并在超过阈值时做一次简单轮转。
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private const long MaxBytes = 4 * 1024 * 1024;

    private readonly string _directory;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _currentFile;

    public FileLogSink(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public void Write(LogLevel level, string message)
    {
        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine(message);
                _writer.Flush();

                if (_writer.BaseStream.Length > MaxBytes)
                {
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

    private void EnsureWriter()
    {
        var file = Path.Combine(_directory, $"launcher-{DateTime.Now:yyyyMMdd}.log");
        if (_writer is not null && string.Equals(_currentFile, file, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _writer?.Dispose();
        _currentFile = file;
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
            _writer?.Dispose();
            _writer = null;
        }
    }
}
