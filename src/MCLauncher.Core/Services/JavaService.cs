using System.Diagnostics;
using System.Text.RegularExpressions;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>
/// Java 运行时的探测与选择。
/// <para>
/// 探测方式：<c>java -XshowSettings:properties -version</c>，属性表打在 **stderr** 上，
/// 因此必须同时接管 stdout 与 stderr。该选项在个别 JRE 上不被识别，
/// 这时退回解析 <c>-version</c> 输出里的版本串。
/// </para>
/// <para>
/// 32 位运行时一律拒绝：现代 Minecraft（1.19+）的 lwjgl natives 只有 64 位版本。
/// </para>
/// </summary>
public sealed partial class JavaService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>扫描深度上限，避免在超大目录里迷路。</summary>
    private const int ScanDepth = 4;

    private const int ScanDirectoryLimit = 512;

    private readonly ILogger _log;
    private readonly string? _managedRuntimeRoot;
    private IReadOnlyList<JavaInfo>? _cache;

    /// <param name="log">日志。</param>
    /// <param name="managedRuntimeRoot">启动器自己管理的 Java 根目录（如 <c>mclcfg\java</c>）；为空时用旧版记录的位置。</param>
    public JavaService(ILogger log, string? managedRuntimeRoot = null)
    {
        _log = log;
        _managedRuntimeRoot = managedRuntimeRoot;
    }

    [GeneratedRegex(@"java\.version\s*=\s*(\S+)")]
    private static partial Regex VersionPropertyPattern();

    [GeneratedRegex(@"java\.home\s*=\s*(.+)")]
    private static partial Regex HomePropertyPattern();

    [GeneratedRegex(@"java\.vendor\s*=\s*(.+)")]
    private static partial Regex VendorPropertyPattern();

    [GeneratedRegex(@"os\.arch\s*=\s*(\S+)")]
    private static partial Regex ArchPropertyPattern();

    [GeneratedRegex(@"sun\.arch\.data\.model\s*=\s*(\S+)")]
    private static partial Regex ArchModelPattern();

    [GeneratedRegex(@"java\.version\.date\s*=\s*(\S+)")]
    private static partial Regex VersionDatePattern();

    /// <summary>从 <c>-version</c> 输出里抓版本号：<c>openjdk version "17.0.1" 2021-10-19</c>。</summary>
    [GeneratedRegex("version \"([^\"]+)\"")]
    private static partial Regex VersionQuotedPattern();

    /// <summary>列出候选 java 可执行文件（仅路径，尚未探测）。</summary>
    public IReadOnlyList<string> EnumerateCandidates()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executable = GameEnvironment.IsWindows ? "java.exe" : "java";

        // ---- 1. JAVA_HOME ----
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            AddIfExists(results, System.IO.Path.Combine(javaHome!, "bin", executable));
        }

        // ---- 2. PATH 里的 java ----
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathVariable.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            AddIfExists(results, System.IO.Path.Combine(segment.Trim(), executable));
        }

        // ---- 3. 常见厂商安装目录、IDE 下载的 JDK ----
        foreach (var root in EnumerateInstallRoots())
        {
            CollectJavaUnder(results, root, executable);
        }

        // ---- 4. 启动器自己管理的 java ----
        foreach (var root in EnumerateManagedRuntimeRoots())
        {
            CollectJavaUnder(results, root, executable);
        }

        return results.ToList();
    }

    /// <summary>探测单个 java 可执行文件；不可用或 32 位时返回 null。</summary>
    public JavaInfo? Probe(string javaPath, CancellationToken ct = default)
    {
        if (!File.Exists(javaPath))
        {
            return null;
        }

        string stderr;
        string stdout;
        var exitCode = RunJava(javaPath, new[] { "-XshowSettings:properties", "-version" }, out stdout, out stderr, ct);

        var combined = stderr + Environment.NewLine + stdout;
        var info = ParseProperties(javaPath, combined);

        if (info is null)
        {
            // 有些 JRE 不认 -XshowSettings，退回普通的 -version。
            exitCode = RunJava(javaPath, new[] { "-version" }, out stdout, out stderr, ct);
            combined = stderr + Environment.NewLine + stdout;
            info = ParseProperties(javaPath, combined, allowFallbackVersion: true);
        }

        if (info is null)
        {
            _log.Warn($"Java 探测失败（exit={exitCode}）：{javaPath}");
            return null;
        }

        if (!info.Is64Bit)
        {
            // 不返回 32 位运行时：上层按"没有可用 Java"处理比选错更安全。
            _log.Warn($"已忽略 32 位 Java：{javaPath}（{info.RawVersion}）");
            return null;
        }

        return info;
    }

    /// <summary>扫描并缓存全部可用 Java（跳过 32 位与探测失败的）。</summary>
    public IReadOnlyList<JavaInfo> Detect(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cache is not null)
        {
            return _cache;
        }

        var candidates = EnumerateCandidates();
        _log.Info($"开始探测 Java，候选路径 {candidates.Count} 个");

        var found = new List<JavaInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var info = Probe(candidate, ct);
            if (info is null)
            {
                continue;
            }

            var key = PathUtil.Normalize(info.Path);
            if (seen.Add(key))
            {
                found.Add(info);
            }
        }

        found.Sort((a, b) => b.SortKey.CompareTo(a.SortKey));
        _cache = found;

        _log.Info(found.Count == 0
            ? "没有发现可用的 Java 运行时"
            : $"发现 {found.Count} 个可用 Java：{string.Join("，", found.Select(j => $"Java {j.MajorVersion}"))}");

        return found;
    }

    /// <summary>
    /// 为指定主版本挑最合适的 Java：
    /// 优先主版本完全一致 → 其次不小于要求的最小版本。
    /// <para>
    /// 找不到「不低版本要求」的 Java 时返回 null，交给上层下载对应版本，而不是退回更旧的 Java。
    /// Java 8 是特殊情况：旧版 Forge / Mod 对 9+ 兼容性很差，没有 8 时也返回 null。
    /// </para>
    /// </summary>
    public JavaInfo? SelectBest(int requiredMajor, IReadOnlyList<JavaInfo>? candidates = null)
    {
        var pool = candidates ?? Detect();
        if (pool.Count == 0)
        {
            return null;
        }

        var exact = pool.Where(j => j.MajorVersion == requiredMajor)
            .OrderByDescending(j => j.SortKey)
            .FirstOrDefault();
        if (exact is not null)
        {
            return exact;
        }

        var newer = pool.Where(j => j.MajorVersion > requiredMajor)
            .OrderBy(j => j.MajorVersion)
            .ThenByDescending(j => j.PatchVersion)
            .FirstOrDefault();

        // Java 8 只认主版本完全匹配，避免拿 17/21 去跑 1.12 的 Forge。
        return requiredMajor == JavaVersionRequirement.DefaultMajorVersion ? null : newer;
    }

    public void Invalidate() => _cache = null;

    /// <summary>把 <c>java.version</c> 归一成 主/次/补丁/第4段。</summary>
    public static (int Major, int Minor, int Patch, int Build) NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return (0, 0, 0, 0);
        }

        var parts = version!.Split('.', '_', '-', '+');
        var numbers = new List<int>(4);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var value))
            {
                numbers.Add(value);
            }
        }

        if (numbers.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        // "1.8.0_292"：第一段是 "1" 时，主版本取第二段。
        if (numbers[0] == 1 && numbers.Count >= 2)
        {
            numbers.RemoveAt(0);
        }

        return (
            numbers[0],
            numbers.Count > 1 ? numbers[1] : 0,
            numbers.Count > 2 ? numbers[2] : 0,
            numbers.Count > 3 ? numbers[3] : 0);
    }

    private JavaInfo? ParseProperties(string javaPath, string output, bool allowFallbackVersion = true)
    {
        var version = Match(VersionPropertyPattern(), output);

        if (version is null && allowFallbackVersion)
        {
            version = Match(VersionQuotedPattern(), output);
        }

        if (version is null)
        {
            return null;
        }

        var archModel = Match(ArchModelPattern(), output);
        var arch = Match(ArchPropertyPattern(), output);

        var is64Bit = archModel == "64"
                      || arch is "amd64" or "x86_64" or "aarch64" or "arm64" or "ppc64le" or "s390x";

        var (major, minor, patch, build) = NormalizeVersion(version);

        return new JavaInfo
        {
            Path = javaPath,
            RawVersion = version,
            MajorVersion = major,
            MinorVersion = minor,
            PatchVersion = patch,
            BuildVersion = build,
            Vendor = Match(VendorPropertyPattern(), output),
            JavaHome = Match(HomePropertyPattern(), output),
            Architecture = arch,
            Is64Bit = is64Bit,
            VersionDate = Match(VersionDatePattern(), output),
        };
    }

    private int RunJava(string javaPath, IReadOnlyList<string> arguments, out string stdout, out string stderr, CancellationToken ct)
    {
        stdout = string.Empty;
        stderr = string.Empty;

        try
        {
            var startInfo = new ProcessStartInfo(javaPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // 进程可能已经退出，忽略。
                }

                _log.Warn($"Java 探测超时（{ProbeTimeout.TotalSeconds:0} 秒）：{javaPath}");
                return -1;
            }

            stdout = SafeGet(stdoutTask);
            stderr = SafeGet(stderrTask);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return -1;
        }
        catch (Exception ex)
        {
            _log.Warn($"无法执行 Java：{javaPath}", ex);
            return -1;
        }
    }

    private static string SafeGet(Task<string> task)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(2)) ? task.Result : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? Match(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void AddIfExists(HashSet<string> results, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                results.Add(System.IO.Path.GetFullPath(path));
            }
        }
        catch (ArgumentException)
        {
            // PATH 里出现非法字符片段时忽略。
        }
    }

    /// <summary>在 root 下按 <c>&lt;dir&gt;/bin/java</c> 与 <c>&lt;dir&gt;/&lt;dir&gt;/bin/java</c> 的形态找 java。</summary>
    private static void CollectJavaUnder(HashSet<string> results, string root, string executable)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var visited = 0;
        try
        {
            foreach (var directory in EnumerateDirectoriesSafe(root, ScanDepth))
            {
                if (++visited > ScanDirectoryLimit)
                {
                    break;
                }

                AddIfExists(results, System.IO.Path.Combine(directory, "bin", executable));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 某些系统目录（如 WindowsApps）没有读取权限，跳过即可。
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            yield return path;

            if (depth >= maxDepth)
            {
                continue;
            }

            IReadOnlyList<string> children;
            try
            {
                children = Directory.GetDirectories(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static IEnumerable<string> EnumerateInstallRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var vendors = new[]
        {
            "Java",
            "Eclipse Adoptium",
            "Eclipse Foundation",
            "Microsoft",
            "Zulu",
            "BellSoft",
            "Amazon Corretto",
            "AdoptOpenJDK",
            "Semeru",
            "SapMachine",
            "JetBrains",
            "Liberica",
        };

        foreach (var basePath in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrEmpty(basePath))
            {
                continue;
            }

            foreach (var vendor in vendors)
            {
                yield return System.IO.Path.Combine(basePath, vendor);
            }
        }

        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return System.IO.Path.Combine(localAppData, "Programs", "Eclipse Adoptium");
            yield return System.IO.Path.Combine(localAppData, "Programs", "Microsoft");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            // IntelliJ / Gradle 下载的 JDK 都落在这里，开发者机器上命中率极高。
            yield return System.IO.Path.Combine(profile, ".jdks");
            yield return System.IO.Path.Combine(profile, ".gradle", "jdks");
            yield return System.IO.Path.Combine(profile, ".sdkman", "candidates", "java");
        }
    }

    private IEnumerable<string> EnumerateManagedRuntimeRoots()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
        {
            yield break;
        }

        // 官方启动器（Microsoft Store 版）下载的 Java 运行时。
        var packages = System.IO.Path.Combine(localAppData, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var package in EnumerateDirectoriesSafe(packages, 0).Skip(1))
            {
                if (System.IO.Path.GetFileName(package).StartsWith("Microsoft.4297127D64EC6", StringComparison.OrdinalIgnoreCase))
                {
                    yield return System.IO.Path.Combine(package, "LocalCache", "Local", "runtime");
                }
            }
        }

        // 本启动器自己管理的 Java。
        yield return _managedRuntimeRoot ?? System.IO.Path.Combine(ConfigRoot(localAppData), "java");
    }

    private static string ConfigRoot(string localAppData) => System.IO.Path.Combine(localAppData, "MCLauncher-Reborn");
}
