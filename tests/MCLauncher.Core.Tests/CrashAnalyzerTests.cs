using MCLauncher.Core.Models;
using MCLauncher.Core.Services;

namespace MCLauncher.Core.Tests;

/// <summary>游戏崩溃分析：报告选取、原因识别与日志拼装。</summary>
public class CrashAnalyzerTests
{
    private static string CreateGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mcl-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateCrashReport(string gameDirectory, string content, DateTime writtenAt)
    {
        var directory = Path.Combine(gameDirectory, "crash-reports");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "crash-2026-10-23_15.00.00-client.txt");
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, writtenAt);
        return path;
    }

    private static void Cleanup(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void 正常退出不产生崩溃报告()
    {
        var directory = CreateGameDirectory();

        try
        {
            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 0,
                LaunchedAt = DateTime.Now.AddMinutes(-5),
                StandardOutput = "Stopping!",
            });

            Assert.Null(report);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void 读取本次启动后的崩溃报告并识别内存不足()
    {
        var directory = CreateGameDirectory();
        var launchedAt = DateTime.Now.AddMinutes(-5);

        try
        {
            CreateCrashReport(directory, """
                ---- Minecraft Crash Report ----
                // Ooh. Shiny.

                Time: 2026-10-23 15:00:00
                Description: Rendering overlay

                java.lang.OutOfMemoryError: Java heap space
                	at net.minecraft.client.renderer.LevelRenderer.renderLevel(LevelRenderer.java:1)
                """, DateTime.Now);

            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 1,
                LaunchedAt = launchedAt,
            });

            Assert.NotNull(report);
            Assert.Equal("游戏崩溃了", report!.Title);
            Assert.Equal("Rendering overlay", report.Description);
            Assert.True(report.HasCrashReport);
            Assert.Contains(report.Causes, cause => cause.Title == "内存不足");
            Assert.Contains("崩溃报告", report.LogText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void 旧的崩溃报告不会触发崩溃界面()
    {
        var directory = CreateGameDirectory();

        try
        {
            CreateCrashReport(directory, "Description: old crash", DateTime.Now.AddHours(-2));

            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 0,
                LaunchedAt = DateTime.Now.AddMinutes(-5),
            });

            Assert.Null(report);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void 没有崩溃报告时从输出识别前置缺失()
    {
        var directory = CreateGameDirectory();

        try
        {
            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 1,
                LaunchedAt = DateTime.Now.AddMinutes(-5),
                StandardOutput = """
                    [main/ERROR]: Missing or unsupported mandatory dependencies:
                    	Mod ID: 'sodium', Requested by: 'iris'
                    net.fabricmc.loader.impl.FormattedException: Mod resolution failed
                    """,
            });

            Assert.NotNull(report);
            Assert.Equal("游戏异常退出", report!.Title);
            Assert.False(report.HasCrashReport);
            Assert.Contains(report.Causes, cause => cause.Title == "模组依赖不满足");
            Assert.Contains("游戏输出", report.LogText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void 模组依赖不满足时附上加载器给出的直接原因()
    {
        var directory = CreateGameDirectory();

        try
        {
            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 1,
                LaunchedAt = DateTime.Now,
                StandardOutput = """
                    [main/WARN]: Mod resolution failed
                    [main/INFO]: Immediate reason: [HARD_DEP geckolib 5.5.7 {depends fabric-api @ [>=0.116.5+26.3]}]
                    net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!
                    """,
            });

            Assert.NotNull(report);
            var cause = Assert.Single(report!.Causes, item => item.Title == "模组依赖不满足");
            Assert.Contains("HARD_DEP geckolib 5.5.7", cause.Advice);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void 识别Java版本不匹配()
    {
        var directory = CreateGameDirectory();

        try
        {
            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 1,
                LaunchedAt = DateTime.Now,
                StandardOutput = "java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main has been compiled by a more recent version",
            });

            Assert.NotNull(report);
            Assert.Contains(report!.Causes, cause => cause.Title == "Java 版本不匹配");
            Assert.StartsWith("java.lang.UnsupportedClassVersionError", report.Description!);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void 未匹配到原因时给出兜底建议()
    {
        var directory = CreateGameDirectory();

        try
        {
            var report = CrashAnalyzer.Analyze(new CrashAnalysisRequest
            {
                GameDirectory = directory,
                ExitCode = 2,
                LaunchedAt = DateTime.Now,
                StandardOutput = "Something unexpected happened",
            });

            Assert.NotNull(report);
            Assert.Single(report!.Causes);
            Assert.Contains("未匹配到", report.Causes[0].Title);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
