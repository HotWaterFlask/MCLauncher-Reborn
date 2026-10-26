using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MCLauncher.Core.Utils;

/// <summary>
/// 在 Windows 命令解释器里执行启动前 / 结束后命令。
/// 命令写法和 <c>cmd.exe</c> 一致，输出逐行回传给调用方。
/// </summary>
public static class ShellCommand
{
    public static async Task<int> RunAsync(
        string command,
        string workingDirectory,
        Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return 0;
        }

        var comspec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comspec))
        {
            comspec = "cmd.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = comspec,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ResolveConsoleEncoding(),
            StandardErrorEncoding = ResolveConsoleEncoding(),
        };

        // 整条命令交给 cmd 自己解析，避免我们提前拆坏引号或重定向符。
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutput?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutput?.Invoke(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动命令解释器");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        // WaitForExitAsync 在进程退出时就会返回，再等一次确保重定向流已读完。
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>cmd 的输出按系统 ANSI 代码页编码（中文 Windows 上是 GBK）。</summary>
    private static Encoding ResolveConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new UTF8Encoding(false);
        }
    }
}
