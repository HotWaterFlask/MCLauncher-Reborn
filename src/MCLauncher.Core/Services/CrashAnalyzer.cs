using System.Text.RegularExpressions;
using MCLauncher.Core.Models;

namespace MCLauncher.Core.Services;

/// <summary>
/// 游戏异常退出后的崩溃分析。
/// <para>
/// 优先读取本次启动之后生成的 <c>crash-reports/crash-*.txt</c>，拿不到时退回进程输出；
/// 再按关键字匹配常见原因（内存、前置缺失、版本不匹配、显卡、权限等），
/// 最后把崩溃报告与输出尾部拼成一段可直接复制给他人看的日志。
/// </para>
/// </summary>
public static class CrashAnalyzer
{
    private const int MaxLogChars = 120_000;

    /// <summary>崩溃报告与启动时间的允许误差（文件时间与本地时钟可能差几秒）。</summary>
    private static readonly TimeSpan LaunchTolerance = TimeSpan.FromSeconds(30);

    private static readonly Regex ExceptionPattern = new(
        @"^(?:[A-Za-z_$][\w$]*\.)+[A-Za-z_$][\w$]*(?:Exception|Error)(?::|\s|$)",
        RegexOptions.Compiled);

    private sealed record Rule(string Title, string Advice, string[] Markers);

    private const string DependencyRuleTitle = "模组依赖不满足";

    private static readonly Rule[] Rules =
    {
        new("内存不足",
            "给游戏分配的内存不够用。请在「实例设置 → 游戏设置」里调大最大内存，或关闭其它占内存的程序后重试。",
            new[] { "OutOfMemoryError", "Java heap space", "GC overhead limit exceeded" }),

        new(DependencyRuleTitle,
            "加载器判定模组之间或与游戏版本不兼容，多数是缺少前置或前置版本不符。请按日志里点名的模组安装 / 升级对应前置，再重新启动。",
            new[]
            {
                "Mod resolution failed", "Incompatible mods found",
                "Some of your mods are incompatible", "HARD_DEP", "FormattedException",
                "Missing or unsupported mandatory dependencies", "ModResolutionException",
                "requires any version of", "Incompatible mod set", "requires a different version",
            }),

        new("模组与游戏版本不匹配",
            "出现找不到类或方法的错误，通常是模组版本与游戏 / 加载器不匹配。请换用适配当前版本的模组，或先移除最近安装的模组。",
            new[]
            {
                "NoSuchMethodError", "NoClassDefFoundError", "ClassNotFoundException",
                "NoSuchFieldError", "IncompatibleClassChangeError",
            }),

        new("Java 版本不匹配",
            "游戏用到了当前 Java 不支持的类文件版本。请在设置里换用更高版本的 Java，或让启动器自动选择。",
            new[] { "UnsupportedClassVersionError", "Unsupported major.minor version" }),

        new("Mixin 注入失败",
            "某个模组注入游戏代码失败，多数是模组之间不兼容或版本不对。可先禁用最近安装的模组，再逐个恢复排查。",
            new[]
            {
                "Mixin apply failed", "MixinApplyError", "InvalidMixinException",
                "MixinTransformerError", "Mixin transformation of",
            }),

        new("存在重复的模组",
            "mods 目录里有同一个模组的多个版本。请在实例管理里删掉多余的旧版本后重试。",
            new[] { "Duplicate mods", "Found duplicate mod", "duplicateMods" }),

        new("显卡 / OpenGL 初始化失败",
            "游戏无法创建渲染上下文。请更新显卡驱动；关闭 MSI Afterburner、RivaTuner 等覆盖层；笔记本可尝试切换到独立显卡。",
            new[]
            {
                "GLFW error", "Pixel format not accelerated", "org.lwjgl.opengl",
                "GLException", "Failed to create window", "OpenGL 1282",
            }),

        new("文件或目录访问失败",
            "游戏读写文件时被拒绝。请检查目录权限、磁盘剩余空间，或把启动器加入杀毒软件白名单。",
            new[]
            {
                "AccessDeniedException", "FileSystemException", "Unable to access",
                "拒绝访问", "The system cannot find the path",
            }),

        new("存档或配置损坏",
            "存档或 level.dat 读取失败。先备份该存档，再尝试在实例管理里修复或删除损坏的存档。",
            new[] { "Corrupted level data", "Invalid player data", "Corrupted chunk" }),

        new("模组之间冲突",
            "堆栈里出现栈溢出，通常是两个模组互相干扰。请先禁用最近安装的模组，确认能启动后再逐个恢复。",
            new[] { "StackOverflowError" }),
    };

    /// <summary>
    /// 分析一次游戏退出。正常退出（退出码 0 且没有新的崩溃报告）返回 <c>null</c>。
    /// </summary>
    public static CrashReport? Analyze(CrashAnalysisRequest request)
    {
        var reportPath = FindCrashReport(request.GameDirectory, request.LaunchedAt);
        var reportText = reportPath is null ? null : TryReadReport(reportPath);

        if (request.ExitCode == 0 && reportText is null)
        {
            return null;
        }

        var output = request.StandardOutput ?? string.Empty;
        var combined = reportText is null ? output : reportText + Environment.NewLine + output;

        var description = ExtractDescription(reportText) ?? ExtractExceptionLine(output);
        var causes = DetectCauses(combined);

        return new CrashReport
        {
            ExitCode = request.ExitCode,
            Title = reportText is null ? "游戏异常退出" : "游戏崩溃了",
            ReportPath = reportPath,
            Description = description,
            LogText = BuildLog(reportText, output),
            Causes = causes,
        };
    }

    // ---------------------------------------------------------------- 崩溃报告

    /// <summary>找到本次启动之后写入的崩溃报告；旧报告不算。</summary>
    private static string? FindCrashReport(string gameDirectory, DateTime launchedAt)
    {
        var directory = Path.Combine(gameDirectory, "crash-reports");
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(directory, "crash-*.txt")
                .Select(path => new FileInfo(path))
                .Where(info => info.LastWriteTime >= launchedAt - LaunchTolerance)
                .OrderByDescending(info => info.LastWriteTime)
                .Select(info => info.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryReadReport(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ExtractDescription(string? reportText)
    {
        if (string.IsNullOrWhiteSpace(reportText))
        {
            return null;
        }

        foreach (var line in reportText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed["Description:".Length..].Trim();
            return value.Length == 0 ? null : Clip(value, 200);
        }

        return null;
    }

    /// <summary>没有崩溃报告时，从游戏输出里找第一条异常声明。</summary>
    private static string? ExtractExceptionLine(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!ExceptionPattern.IsMatch(trimmed))
            {
                continue;
            }

            return Clip(trimmed, 240);
        }

        return null;
    }

    // ---------------------------------------------------------------- 原因识别

    private static IReadOnlyList<CrashCause> DetectCauses(string text)
    {
        var causes = new List<CrashCause>();

        foreach (var rule in Rules)
        {
            if (!rule.Markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            causes.Add(new CrashCause { Title = rule.Title, Advice = rule.Advice });
        }

        // 依赖类失败在日志里通常有一行「Immediate reason / requires ...」，
        // 直接把它附在建议后面，用户一眼就知道缺哪个前置。
        var index = causes.FindIndex(c => c.Title == DependencyRuleTitle);
        if (index >= 0)
        {
            var detail = FindDependencyDetail(text);
            if (detail is not null)
            {
                causes[index] = new CrashCause
                {
                    Title = DependencyRuleTitle,
                    Advice = causes[index].Advice + Environment.NewLine + "日志中的直接原因：" + detail,
                };
            }
        }

        if (causes.Count == 0)
        {
            causes.Add(new CrashCause
            {
                Title = "未匹配到已知的崩溃特征",
                Advice = "请查看下方日志里的第一条报错，或把日志发到模组页面 / 社区求助。"
                         + "也可以先在实例管理里禁用最近安装的模组再试。",
            });
        }

        return causes;
    }

    /// <summary>从日志里抽出加载器给出的依赖提示行。</summary>
    private static string? FindDependencyDetail(string text)
    {
        string[] markers =
        {
            "Immediate reason:", "requires any version of",
            "requires a different version", "HARD_DEP", " depends ",
        };

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 4 ||
                !markers.Any(marker => trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return Clip(trimmed, 220);
        }

        return null;
    }

    // ---------------------------------------------------------------- 日志

    private static string BuildLog(string? reportText, string output)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(reportText))
        {
            parts.Add("===== 崩溃报告 =====");
            parts.Add(reportText.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            parts.Add(reportText is null ? "===== 游戏输出 =====" : "===== 游戏输出（末尾）=====");
            parts.Add(reportText is null ? output.TrimEnd() : TailLines(output, 500));
        }

        return Limit(string.Join(Environment.NewLine + Environment.NewLine, parts), MaxLogChars);
    }

    private static string TailLines(string text, int count)
    {
        var lines = text.Split('\n');
        if (lines.Length <= count)
        {
            return text.TrimEnd();
        }

        return string.Join('\n', lines[^count..]).TrimEnd();
    }

    /// <summary>超长日志保留头尾，中间省略，避免界面卡顿。</summary>
    private static string Limit(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var half = max / 2;
        return text[..half]
               + Environment.NewLine
               + "……（日志过长，中间部分已省略）……"
               + Environment.NewLine
               + text[^half..];
    }

    private static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
