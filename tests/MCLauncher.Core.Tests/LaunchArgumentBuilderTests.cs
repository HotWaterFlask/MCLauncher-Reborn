using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class LaunchArgumentBuilderTests
{
    private const string ModernJson = """
    {
      "id": "1.20.4",
      "mainClass": "net.minecraft.client.main.Main",
      "type": "release",
      "assets": "12",
      "arguments": {
        "jvm": [
          {"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"-XX:HeapDumpPath=foo.heapdump"},
          {"rules":[{"action":"allow","os":{"name":"osx"}}],"value":["-XstartOnFirstThread"]},
          "-Djava.library.path=${natives_directory}",
          "-Dminecraft.launcher.brand=${launcher_name}",
          "-cp",
          "${classpath}"
        ],
        "game": [
          "--username","${auth_player_name}",
          "--version","${version_name}",
          "--gameDir","${game_directory}",
          "--assetsDir","${assets_root}",
          "--assetIndex","${assets_index_name}",
          "--uuid","${auth_uuid}",
          "--accessToken","${auth_access_token}",
          "--clientId","${clientid}",
          "--xuid","${auth_xuid}",
          "--userType","${user_type}",
          {"rules":[{"action":"allow","features":{"has_custom_resolution":true}}],
           "value":["--width","${resolution_width}","--height","${resolution_height}"]}
        ]
      },
      "libraries": [
        {"name":"com.google.code.gson:gson:2.10.1"},
        {"name":"org.lwjgl:lwjgl:3.3.2",
         "downloads":{"classifiers":{"natives-windows":{"sha1":"b","size":1,"url":"https://example.invalid/n.jar"}}}},
        {"name":"com.example:只在本平台用:1.0","rules":[{"action":"allow","os":{"name":"osx"}}]}
      ]
    }
    """;

    private const string LegacyJson = """
    {
      "id": "1.7.10",
      "mainClass": "net.minecraft.client.main.Main",
      "type": "release",
      "assets": "1.7.10",
      "minecraftArguments": "${auth_player_name} ${auth_session} --gameDir ${game_directory} --assetsDir ${game_assets} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userProperties ${user_properties} --userType ${user_type}",
      "libraries": [{"name":"com.google.code.gson:gson:2.2.4"}]
    }
    """;

    private static GameVersionJson Parse(string json) => JsonUtil.Deserialize<GameVersionJson>(json)!;

    /// <summary>IReadOnlyList 没有 IndexOf，测试里自己补一个。</summary>
    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static LaunchContext CreateContext(
        string versionJson,
        LaunchOptions options,
        Account account,
        string gameDirectory,
        string? workingDirectory = null)
    {
        var version = Parse(versionJson);
        return new LaunchContext
        {
            Version = version,
            Account = account,
            Options = options,
            JavaPath = "C:/java/bin/java.exe",
            VersionId = version.Id!,
            GameDirectory = gameDirectory,
            WorkingDirectory = workingDirectory,
            AssetsRoot = Path.Combine(gameDirectory, "assets"),
            NativesDirectory = Path.Combine(gameDirectory, "versions", version.Id!, "natives-windows"),
            LibrariesDirectory = Path.Combine(gameDirectory, "libraries"),
            ClientJarPath = Path.Combine(gameDirectory, "versions", version.Id!, version.Id! + ".jar"),
            AssetsIndexName = version.Assets,
        };
    }

    private static LaunchCommand Build(string json, LaunchOptions options, string gameDir = @"C:\mc")
    {
        var context = CreateContext(json, options, Account.CreateOffline("Player"), gameDir);
        return new LaunchArgumentBuilder(new TestLog()).Build(context);
    }

    [Fact]
    public void 现代版本的游戏参数被完整替换()
    {
        var command = Build(ModernJson, new LaunchOptions());

        var game = command.GameArguments;
        Assert.Contains("--username", game);
        Assert.Equal("Player", game[IndexOf(game, "--username") + 1]);
        Assert.Equal("1.20.4", game[IndexOf(game, "--version") + 1]);
        Assert.Equal(@"C:\mc", game[IndexOf(game, "--gameDir") + 1]);
        Assert.Equal("12", game[IndexOf(game, "--assetIndex") + 1]);
        Assert.Equal("legacy", game[IndexOf(game, "--userType") + 1]);

        Assert.Equal("net.minecraft.client.main.Main", command.MainClass);
    }

    [Fact]
    public void 不再残留任何未替换的变量占位符()
    {
        var command = Build(ModernJson, new LaunchOptions());

        foreach (var argument in command.ToArgumentList())
        {
            Assert.DoesNotContain("${", argument);
        }
    }

    [Fact]
    public void 离线账号的客户端标识按空串处理而不是留字面量()
    {
        var command = Build(ModernJson, new LaunchOptions());
        var game = command.GameArguments;

        Assert.Equal(string.Empty, game[IndexOf(game, "--clientId") + 1]);
        Assert.Equal(string.Empty, game[IndexOf(game, "--xuid") + 1]);
    }

    [Fact]
    public void 微软账号会带上xuid与clientid()
    {
        var account = new Account
        {
            Kind = AccountKind.Microsoft,
            Name = "Steve",
            Uuid = "5627dd98e6be3c21b8a8e92344183641",
            AccessToken = "token",
            Xuid = "1234567890",
            ClientId = "client-id",
        };

        var context = CreateContext(ModernJson, new LaunchOptions(), account, @"C:\mc");
        var command = new LaunchArgumentBuilder(new TestLog()).Build(context);

        var game = command.GameArguments;
        Assert.Equal("1234567890", game[IndexOf(game, "--xuid") + 1]);
        Assert.Equal("client-id", game[IndexOf(game, "--clientId") + 1]);
        Assert.Equal("msa", game[IndexOf(game, "--userType") + 1]);
    }

    [Fact]
    public void JVM参数的规则按当前平台生效()
    {
        var command = Build(ModernJson, new LaunchOptions());

        if (GameEnvironment.IsWindows)
        {
            Assert.Contains("-XX:HeapDumpPath=foo.heapdump", command.JvmArguments);
            Assert.DoesNotContain("-XstartOnFirstThread", command.JvmArguments);
        }
        else if (GameEnvironment.IsMacOs)
        {
            Assert.Contains("-XstartOnFirstThread", command.JvmArguments);
            Assert.DoesNotContain("-XX:HeapDumpPath=foo.heapdump", command.JvmArguments);
        }
    }

    [Fact]
    public void classpath是库在前jar在后且排除natives()
    {
        var command = Build(ModernJson, new LaunchOptions());
        var entries = command.Classpath.Split(Path.PathSeparator);

        Assert.Equal(2, entries.Length);
        Assert.EndsWith("gson-2.10.1.jar", entries[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("1.20.4.jar", entries[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(entries, e => e.Contains("natives", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 不适用当前平台的库不进classpath()
    {
        var command = Build(ModernJson, new LaunchOptions());
        Assert.DoesNotContain(command.Classpath, "只在本平台用");
    }

    [Fact]
    public void 未设置分辨率时width与height参数不出现()
    {
        var command = Build(ModernJson, new LaunchOptions());
        Assert.DoesNotContain("--width", command.GameArguments);
    }

    [Fact]
    public void 设置了分辨率时现代版本也会带上width与height()
    {
        var command = Build(ModernJson, new LaunchOptions { Width = 1280, Height = 720 });
        var game = command.GameArguments;

        Assert.Equal("1280", game[IndexOf(game, "--width") + 1]);
        Assert.Equal("720", game[IndexOf(game, "--height") + 1]);
    }

    [Fact]
    public void 旧版minecraftArguments被正确切分与替换()
    {
        var command = Build(LegacyJson, new LaunchOptions { Width = 854, Height = 480 });
        var game = command.GameArguments;

        Assert.Equal("Player", game[0]);
        Assert.Equal("0", game[1]);
        Assert.Contains("--gameDir", game);
        Assert.Equal(@"C:\mc", game[IndexOf(game, "--gameDir") + 1]);

        // 旧版 JSON 没有规则化的分辨率参数，必须自己补。
        Assert.Equal("854", game[IndexOf(game, "--width") + 1]);
        Assert.Equal("480", game[IndexOf(game, "--height") + 1]);

        foreach (var argument in game)
        {
            Assert.DoesNotContain("${", argument);
        }
    }

    [Fact]
    public void 旧版使用默认JVM参数并带上classpath()
    {
        var command = Build(LegacyJson, new LaunchOptions());
        var jvm = command.JvmArguments;

        Assert.Contains("-Djava.library.path=" + Path.Combine(@"C:\mc", "versions", "1.7.10", "natives-windows"), jvm);
        var cpIndex = IndexOf(jvm, "-cp");
        Assert.True(cpIndex >= 0, "旧版参数里必须有 -cp");
        Assert.Equal(command.Classpath, jvm[cpIndex + 1]);
    }

    [Fact]
    public void 内存参数始终存在()
    {
        var command = Build(ModernJson, new LaunchOptions { MaxMemoryMb = 4096, MinMemoryMb = 1024 });
        Assert.Contains("-Xmx4096M", command.JvmArguments);
        Assert.Contains("-Xms1024M", command.JvmArguments);
    }

    [Fact]
    public void 启动器品牌变量被替换()
    {
        var command = Build(ModernJson, new LaunchOptions { LauncherName = "MyLauncher" });
        Assert.Contains("-Dminecraft.launcher.brand=MyLauncher", command.JvmArguments);
    }

    [Fact]
    public void 服务器参数被追加()
    {
        var command = Build(ModernJson, new LaunchOptions { ServerIp = "mc.example.com", ServerPort = 25566 });
        var game = command.GameArguments;

        Assert.Equal("mc.example.com", game[IndexOf(game, "--server") + 1]);
        Assert.Equal("25566", game[IndexOf(game, "--port") + 1]);
    }

    [Fact]
    public void 一点二以上改用快速加入参数()
    {
        var options = new LaunchOptions
        {
            ServerIp = "mc.example.com",
            ServerPort = 25566,
            GameVersion = "1.20.4",
        };

        var game = Build(ModernJson, options).GameArguments;

        Assert.Equal("mc.example.com:25566", game[IndexOf(game, "--quickPlayMultiplayer") + 1]);
        Assert.Equal(-1, IndexOf(game, "--server"));
    }

    [Fact]
    public void 旧版本继续使用服务器与端口参数()
    {
        var options = new LaunchOptions
        {
            ServerIp = "mc.example.com",
            ServerPort = 25566,
            GameVersion = "1.19.4",
        };

        var game = Build(ModernJson, options).GameArguments;

        Assert.Equal("mc.example.com", game[IndexOf(game, "--server") + 1]);
        Assert.Equal(-1, IndexOf(game, "--quickPlayMultiplayer"));
    }

    [Fact]
    public void 快速加入的IPv6地址带方括号()
    {
        var options = new LaunchOptions
        {
            ServerIp = "2408:8207::1",
            ServerPort = 25566,
            GameVersion = "1.21.1",
        };

        var game = Build(ModernJson, options).GameArguments;

        Assert.Equal("[2408:8207::1]:25566", game[IndexOf(game, "--quickPlayMultiplayer") + 1]);
    }

    [Fact]
    public void 自定义参数支持变量且带引号的参数不会被切错()
    {
        var options = new LaunchOptions
        {
            ExtraJvmArgs = "-Dmy.dir=\"C:/path with space\" -Dver=${version_name}",
            ExtraGameArgs = "--fullscreen",
        };

        var command = Build(ModernJson, options);

        Assert.Contains(@"-Dmy.dir=C:/path with space", command.JvmArguments);
        Assert.Contains("-Dver=1.20.4", command.JvmArguments);
        Assert.Contains("--fullscreen", command.GameArguments);
    }

    [Fact]
    public void 参数切分器处理引号与多余空格()
    {
        var parts = LaunchArgumentBuilder.SplitArguments("""  -a   "b c"  --d="e f" """);

        Assert.Equal(new[] { "-a", "b c", "--d=e f" }, parts);
    }

    [Fact]
    public void 缺少mainClass时给出明确错误()
    {
        var context = CreateContext("""{"id":"x","libraries":[]}""", new LaunchOptions(), Account.CreateOffline("P"), @"C:\mc");
        Assert.Throws<InvalidDataException>(() => new LaunchArgumentBuilder(new TestLog()).Build(context));
    }

    [Fact]
    public void 未提供的变量按空串处理并告警()
    {
        const string json = """
        {"id":"x","mainClass":"M","arguments":{"game":["--unknown","${not_a_real_var}"],"jvm":[]}}
        """;

        var log = new TestLog();
        var context = CreateContext(json, new LaunchOptions(), Account.CreateOffline("P"), @"C:\mc");
        var command = new LaunchArgumentBuilder(log).Build(context);

        Assert.Contains("--unknown", command.GameArguments);
        Assert.Equal(string.Empty, command.GameArguments[1]);
        Assert.True(log.Contains("not_a_real_var"), "应记录未识别的变量");
    }

    [Fact]
    public void 单人存档快速游玩参数被追加()
    {
        var options = new LaunchOptions
        {
            QuickPlayMode = QuickPlayMode.Singleplayer,
            QuickPlayWorld = "我的世界",
            GameVersion = "1.20.4",
        };

        var game = Build(ModernJson, options).GameArguments;

        Assert.Equal("我的世界", game[IndexOf(game, "--quickPlaySingleplayer") + 1]);
        Assert.Equal(-1, IndexOf(game, "--quickPlayMultiplayer"));
    }

    [Fact]
    public void Realms快速游玩参数被追加()
    {
        var options = new LaunchOptions
        {
            QuickPlayMode = QuickPlayMode.Realms,
            QuickPlayRealmsId = "1234567",
            GameVersion = "1.21.1",
        };

        var game = Build(ModernJson, options).GameArguments;

        Assert.Equal("1234567", game[IndexOf(game, "--quickPlayRealms") + 1]);
    }

    [Fact]
    public void 旧版本的快速游玩设置被忽略()
    {
        var options = new LaunchOptions
        {
            QuickPlayMode = QuickPlayMode.Singleplayer,
            QuickPlayWorld = "world",
            GameVersion = "1.19.4",
        };

        var game = Build(ModernJson, options).GameArguments;

        Assert.Equal(-1, IndexOf(game, "--quickPlaySingleplayer"));
        Assert.Equal(-1, IndexOf(game, "--quickPlayRealms"));
    }

    [Fact]
    public void 工作目录环境变量优先级与包装命令被传递()
    {
        var options = new LaunchOptions
        {
            EnvironmentVariables = new Dictionary<string, string> { ["MY_VAR"] = "值" },
            ProcessPriority = GameProcessPriority.High,
            WrapperCommand = "wrapper.exe --flag",
        };

        var context = CreateContext(
            ModernJson,
            options,
            Account.CreateOffline("P"),
            @"C:\mc",
            workingDirectory: @"D:\run");
        var command = new LaunchArgumentBuilder(new TestLog()).Build(context);

        Assert.Equal(@"D:\run", command.WorkingDirectory);
        Assert.Equal("值", command.Environment["MY_VAR"]);
        Assert.Equal(GameProcessPriority.High, command.Priority);
        Assert.Equal("wrapper.exe --flag", command.WrapperCommand);
    }

    [Fact]
    public void 未设置工作目录时回退到实例目录()
    {
        var command = Build(ModernJson, new LaunchOptions(), @"C:\mc");
        Assert.Equal(@"C:\mc", command.WorkingDirectory);
    }
}
