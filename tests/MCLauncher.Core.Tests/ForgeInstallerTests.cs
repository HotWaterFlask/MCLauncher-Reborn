using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;
using Xunit;

namespace MCLauncher.Core.Tests;

/// <summary>
/// Forge / NeoForge 安装器的本地解析与处理器流程。
/// 全部用合成的安装器 jar，不访问网络、不依赖本机 Java。
/// </summary>
public sealed class ForgeInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mcl-forge-" + Guid.NewGuid().ToString("N"));

    private readonly TestLog _log = new();
    private readonly HttpDownloader _downloader;
    private readonly ForgeInstallerService _service;

    public ForgeInstallerTests()
    {
        Directory.CreateDirectory(_root);

        _downloader = new HttpDownloader(_log);
        var resolver = new DownloadSourceResolver(_log) { PreferMirror = false };
        var batch = new DownloadBatch(_downloader, _log);
        var repository = new VersionRepository(new VersionResolver(_log), _log);
        var installer = new GameInstaller(_downloader, resolver, batch, repository, _log);

        _service = new ForgeInstallerService(
            _downloader, resolver, installer, new JavaService(_log), _log);
    }

    public void Dispose()
    {
        _downloader.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试清理失败不影响结果。
        }
    }

    [Fact]
    public void Maven坐标支持扩展名与分类器()
    {
        Assert.Equal(
            "de/oceanlabs/mcp/mcp_config/1.20.1-20230612.114412/mcp_config-1.20.1-20230612.114412-mappings.txt",
            ForgeInstallerService.ArtifactPath(
                "de.oceanlabs.mcp:mcp_config:1.20.1-20230612.114412:mappings@txt"));

        Assert.Equal(
            "net/minecraftforge/ForgeAutoRenamingTool/0.1.22/ForgeAutoRenamingTool-0.1.22-all.jar",
            ForgeInstallerService.ArtifactPath("net.minecraftforge:ForgeAutoRenamingTool:0.1.22:all"));

        Assert.Equal(
            "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar",
            ForgeInstallerService.ArtifactPath("net.minecraftforge:forge:1.20.1-47.2.0:client"));
    }

    [Fact]
    public void 令牌替换支持变量与反斜杠转义()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ROOT"] = @"C:\game root",
            ["MC"] = "1.20.1",
        };

        Assert.Equal(
            @"C:\game root/libraries/net/minecraftforge/forge/1.20.1-47.2.0/forge.jar",
            ForgeInstallerService.ReplaceTokens(
                "{ROOT}/libraries/net/minecraftforge/forge/{MC}-47.2.0/forge.jar", variables));

        Assert.Equal("{ROOT}", ForgeInstallerService.ReplaceTokens(@"\{ROOT\}", variables));

        Assert.Throws<InvalidDataException>(() =>
            ForgeInstallerService.ReplaceTokens("{MISSING}", variables));
    }

    [Fact]
    public void 处理器参数按字面量扩展名与变量解析()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ROOT"] = @"C:\game",
        };

        static string Artifact(string coordinate) => "ARTIFACT:" + coordinate;

        Assert.Equal(@"C:\game/libraries/x.jar",
            ForgeInstallerService.ParseLiteral("{ROOT}/libraries/x.jar", variables, Artifact));
        Assert.Equal("--side",
            ForgeInstallerService.ParseLiteral("'--side'", variables, Artifact));
        Assert.Equal("ARTIFACT:net.minecraft:client:1.20.1:mappings@txt",
            ForgeInstallerService.ParseLiteral(
                "[net.minecraft:client:1.20.1:mappings@txt]", variables, Artifact));
    }

    [Fact]
    public async Task 处理器输出已齐全时不需要Java也不下载客户端()
    {
        const string artifact = "net/minecraftforge/forge/1.20.1-9.9.9/forge-1.20.1-9.9.9-client.jar";
        var outputPath = GameInstaller.LibraryFilePath(_root, artifact);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var payload = Encoding.UTF8.GetBytes("patched-client");
        await File.WriteAllBytesAsync(outputPath, payload, CancellationToken.None);
        var sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();

        var installerPath = Path.Combine(_root, "forge-installer.jar");
        CreateInstaller(installerPath,
            CreateProfile(spec: 1, version: "1.20.1-forge-9.9.9", minecraft: "1.20.1",
                data: new Dictionary<string, object>
                {
                    ["PATCHED"] = new { client = "[net.minecraftforge:forge:1.20.1-9.9.9:client]" },
                    ["PATCHED_SHA"] = new { client = $"'{sha1}'" },
                },
                processors: new object[]
                {
                    new
                    {
                        sides = new[] { "client" },
                        jar = "net.minecraftforge:binarypatcher:1.1.1",
                        classpath = Array.Empty<string>(),
                        args = new[] { "--clean", "{MC_SRG}", "--output", "{PATCHED}" },
                        outputs = new Dictionary<string, string> { ["{PATCHED}"] = "{PATCHED_SHA}" },
                    },
                },
                libraries: Array.Empty<object>()),
            CreateVersionJson("1.20.1-forge-9.9.9", "1.20.1"));

        var result = await _service.PrepareAsync(
            ModLoaderKind.Forge,
            _root,
            "1.20.1",
            "9.9.9",
            new GameVersionJson { Id = "1.20.1" },
            installerPath,
            clientJarPath: null,
            new InstallOptions(),
            progress: null,
            CancellationToken.None);

        Assert.Equal("1.20.1-forge-9.9.9", result.Id);
        Assert.Equal("1.20.1", result.InheritsFrom);

        // 没有生成原版基底目录，也没有下载客户端 jar。
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", "1.20.1")));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions")));
    }

    [Fact]
    public async Task 老式安装器解出通用jar并返回内嵌版本信息()
    {
        const string universal = "net/minecraftforge/forge/1.12.2-9.9.9/forge-1.12.2-9.9.9.jar";
        var universalBytes = Encoding.UTF8.GetBytes("forge-universal");

        var installerPath = Path.Combine(_root, "forge-installer.jar");
        CreateInstaller(installerPath,
            CreateProfile(spec: 0, version: "1.12.2-forge-9.9.9", minecraft: "1.12.2",
                data: new Dictionary<string, object>(),
                processors: Array.Empty<object>(),
                libraries: Array.Empty<object>(),
                install: new
                {
                    path = "net.minecraftforge:forge:1.12.2-9.9.9",
                    filePath = "forge-universal.jar",
                    minecraft = "1.12.2",
                },
                versionInfo: new
                {
                    id = "1.12.2-forge-9.9.9",
                    inheritsFrom = "1.12.2",
                    mainClass = "net.minecraft.launchwrapper.Launch",
                }),
            extraEntries: new Dictionary<string, byte[]>
            {
                ["forge-universal.jar"] = universalBytes,
            });

        var result = await _service.PrepareAsync(
            ModLoaderKind.Forge,
            _root,
            "1.12.2",
            "9.9.9",
            new GameVersionJson { Id = "1.12.2" },
            installerPath,
            clientJarPath: null,
            new InstallOptions(),
            progress: null,
            CancellationToken.None);

        Assert.Equal("1.12.2-forge-9.9.9", result.Id);
        Assert.Equal("net.minecraft.launchwrapper.Launch", result.MainClass);

        var installed = GameInstaller.LibraryFilePath(_root, universal);
        Assert.True(File.Exists(installed));
        Assert.Equal(universalBytes, await File.ReadAllBytesAsync(installed, CancellationToken.None));
    }

    [Fact]
    public async Task 内嵌maven制品会在不联网的情况下展开()
    {
        const string artifact = "net/minecraftforge/forge/1.12.2-9.9.9/forge-1.12.2-9.9.9.jar";
        var payload = Encoding.UTF8.GetBytes("embedded-forge");

        var installerPath = Path.Combine(_root, "forge-installer.jar");
        CreateInstaller(installerPath,
            CreateProfile(spec: 0, version: "1.12.2-forge-9.9.9", minecraft: "1.12.2",
                data: new Dictionary<string, object>(),
                processors: Array.Empty<object>(),
                libraries: new object[]
                {
                    new
                    {
                        name = "net.minecraftforge:forge:1.12.2-9.9.9",
                        downloads = new
                        {
                            artifact = new
                            {
                                path = artifact,
                                url = string.Empty,
                                sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant(),
                                size = payload.Length,
                            },
                        },
                    },
                },
                json: "/version.json"),
            CreateVersionJson("1.12.2-forge-9.9.9", "1.12.2"),
            new Dictionary<string, byte[]>
            {
                ["maven/" + artifact] = payload,
            });

        var result = await _service.PrepareAsync(
            ModLoaderKind.Forge,
            _root,
            "1.12.2",
            "9.9.9",
            new GameVersionJson { Id = "1.12.2" },
            installerPath,
            clientJarPath: null,
            new InstallOptions(),
            progress: null,
            CancellationToken.None);

        Assert.Equal("1.12.2-forge-9.9.9", result.Id);

        var installed = GameInstaller.LibraryFilePath(_root, artifact);
        Assert.True(File.Exists(installed));
        Assert.Equal(payload, await File.ReadAllBytesAsync(installed, CancellationToken.None));

        // 内嵌制品已有，无 URL 的库不应该再去拼一个会 404 的地址。
        Assert.False(_log.Contains("无法解析下载地址"));
        Assert.False(_log.Contains("下载游戏文件"));
    }

    // ------------------------------------------------------------------ 构造安装器

    private static string CreateProfile(
        int spec,
        string version,
        string minecraft,
        Dictionary<string, object> data,
        object[] processors,
        object[] libraries,
        string json = "/version.json",
        object? install = null,
        object? versionInfo = null)
    {
        var profile = new Dictionary<string, object>
        {
            ["spec"] = spec,
            ["profile"] = "forge",
            ["version"] = version,
            ["minecraft"] = minecraft,
            ["json"] = json,
            ["data"] = data,
            ["processors"] = processors,
            ["libraries"] = libraries,
        };

        if (install is not null)
        {
            profile["install"] = install;
        }

        if (versionInfo is not null)
        {
            profile["versionInfo"] = versionInfo;
        }

        return JsonUtil.Serialize(profile);
    }

    private static string CreateVersionJson(string id, string inheritsFrom) =>
        JsonUtil.Serialize(new Dictionary<string, object>
        {
            ["id"] = id,
            ["inheritsFrom"] = inheritsFrom,
            ["mainClass"] = "cpw.mods.bootstraplauncher.BootstrapLauncher",
        });

    private static void CreateInstaller(
        string path,
        string installProfile,
        string? versionJson = null,
        Dictionary<string, byte[]>? extraEntries = null)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteEntry(archive, "install_profile.json", installProfile);

        if (!string.IsNullOrEmpty(versionJson))
        {
            WriteEntry(archive, "version.json", versionJson!);
        }

        foreach (var (name, bytes) in extraEntries ?? new Dictionary<string, byte[]>())
        {
            WriteEntry(archive, name, bytes);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string text) =>
        WriteEntry(archive, name, Encoding.UTF8.GetBytes(text));

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
