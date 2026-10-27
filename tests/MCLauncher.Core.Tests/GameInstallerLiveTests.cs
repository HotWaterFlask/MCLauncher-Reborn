using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

/// <summary>
/// M4 的硬验证：真实下载一个完整版本，核对磁盘布局、校验、幂等。
/// 远古版本（b1.7.3）覆盖旧式库、${arch} natives、pre-1.6 索引的 map_to_resources 展开；
/// 现代版本（1.13.2）覆盖结构化参数版本 + 现代 natives 分类器。
/// 这些用例会访问外网并下载数十 MB，默认按名称过滤时可跳过。
/// </summary>
public sealed class GameInstallerLiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(12);

    internal static string Describe(InstallReport report)
    {
        var failures = string.Join("；", report.Failures.Take(8).Select(f => $"{f.Name} → {f.Error}"));
        return $"下载 {report.Downloaded}，跳过 {report.Skipped}，失败 {report.Failed}；{failures}";
    }

    [Fact]
    public async Task 完整安装远古版本并校验与幂等()
    {
        using var temp = new TempDirectory();
        using var stack = LiveTestStack.Create(Path.Combine(temp.Path, ".cache"));
        using var cts = new CancellationTokenSource(Timeout);

        const string versionId = "b1.7.3";
        var version = await stack.FetchVersionAsync(versionId, cts.Token);

        var progress = new List<InstallProgress>();
        var progressHandler = new InlineProgress<InstallProgress>(p =>
        {
            lock (progress)
            {
                progress.Add(p);
            }
        });

        var report = await stack.Installer.InstallVersionAsync(
            temp.Path,
            version,
            new InstallOptions { DownloadAssets = true, MaxParallel = 16 },
            progressHandler,
            cts.Token);

        Assert.True(report.Success, Describe(report));
        Assert.True(report.Downloaded > 0, "应该确实下载了新文件");

        // ---- 磁盘布局 ----
        Assert.True(File.Exists(VersionRepository.VersionJsonPath(temp.Path, versionId)), "版本 JSON 缺失");
        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, versionId)), "客户端 jar 缺失");

        var libraryJars = Directory.GetFiles(
            GameInstaller.LibrariesDirectory(temp.Path), "*.jar", SearchOption.AllDirectories);
        Assert.NotEmpty(libraryJars);
        Assert.True(libraryJars.Length >= 5, $"依赖库数量偏少：{libraryJars.Length}");

        var nativesDirectory = VersionRepository.NativesDirectory(temp.Path, versionId);
        Assert.True(Directory.Exists(nativesDirectory), "natives 目录缺失");
        Assert.NotEmpty(Directory.GetFiles(nativesDirectory, "*.dll", SearchOption.AllDirectories));

        Assert.True(File.Exists(Path.Combine(temp.Path, "launcher_profiles.json")), "launcher_profiles.json 缺失");

        // ---- 资源索引与对象 ----
        var indexPath = GameInstaller.AssetsIndexPath(temp.Path, version.Assets!);
        Assert.True(File.Exists(indexPath), "资源索引缺失");

        var index = JsonUtil.DeserializeFromFile<AssetIndex>(indexPath)!;
        Assert.NotEmpty(index.Objects);
        Assert.True(index.MapToResources, $"pre-1.6 索引应带 map_to_resources 标记（id={version.Assets}）");

        foreach (var obj in index.Objects.Values.Take(5))
        {
            Assert.True(File.Exists(GameInstaller.AssetsObjectPath(temp.Path, obj.Hash)), $"资源对象缺失：{obj.Hash}");
        }

        // map_to_resources 展开
        Assert.True(Directory.Exists(GameInstaller.ResourcesDirectory(temp.Path)), "resources 目录缺失");

        // ---- 进度确实按阶段上报 ----
        lock (progress)
        {
            Assert.Contains(progress, p => p.Stage == "下载资源文件" && p.Total > 100);
            Assert.Contains(progress, p => p.Stage == "解压 natives");
            Assert.Contains(progress, p => p.Stage == "完成");
        }

        // ---- 深度校验应全部通过 ----
        var verify = await stack.Installer.VerifyAsync(temp.Path, versionId, deep: true, ct: cts.Token);
        Assert.True(verify.Success, "校验发现缺失：" + string.Join("；", verify.Missing.Take(10)));

        // ---- 幂等：再次安装应一个文件都不下载 ----
        var again = await stack.Installer.InstallVersionAsync(
            temp.Path,
            version,
            new InstallOptions { DownloadAssets = true, MaxParallel = 16 },
            null,
            cts.Token);

        Assert.True(again.Success, Describe(again));
        Assert.Equal(0, again.Downloaded);
        Assert.True(again.Skipped > 100);
    }

    [Fact]
    public async Task 安装现代版本并跳过资源下载()
    {
        using var temp = new TempDirectory();
        using var stack = LiveTestStack.Create(Path.Combine(temp.Path, ".cache"));
        using var cts = new CancellationTokenSource(Timeout);

        const string versionId = "1.13.2";
        var version = await stack.FetchVersionAsync(versionId, cts.Token);

        var options = new InstallOptions { DownloadAssets = false, MaxParallel = 16 };
        var report = await stack.Installer.InstallVersionAsync(temp.Path, version, options, null, cts.Token);

        Assert.True(report.Success, Describe(report));

        // 结构化参数版本应解析到 arguments 与 javaVersion。
        Assert.NotNull(version.Arguments);
        Assert.False(version.UsesLegacyArguments);
        Assert.True(version.RequiredJavaMajor >= 8);

        Assert.True(File.Exists(VersionRepository.VersionJarPath(temp.Path, versionId)));
        Assert.True(File.Exists(GameInstaller.AssetsIndexPath(temp.Path, version.Assets!)), "资源索引缺失");

        // 现代版本的 natives 走 downloads.classifiers（natives-windows 等），必须解压成功。
        var nativesDirectory = VersionRepository.NativesDirectory(temp.Path, versionId);
        Assert.True(Directory.Exists(nativesDirectory));
        Assert.NotEmpty(Directory.GetFiles(nativesDirectory, "*.dll", SearchOption.AllDirectories));

        // 未下载资源对象是预期行为。
        var index = JsonUtil.DeserializeFromFile<AssetIndex>(GameInstaller.AssetsIndexPath(temp.Path, version.Assets!))!;
        Assert.NotEmpty(index.Objects);
        Assert.False(File.Exists(GameInstaller.AssetsObjectPath(temp.Path, index.Objects.Values.First().Hash)));

        var again = await stack.Installer.InstallVersionAsync(temp.Path, version, options, null, cts.Token);
        Assert.True(again.Success, Describe(again));
        Assert.Equal(0, again.Downloaded);
    }
}
