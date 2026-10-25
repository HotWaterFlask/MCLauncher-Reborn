using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCLauncher.App.Themes;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Network;
using MCLauncher.Core.Services;

namespace MCLauncher.App;

/// <summary>
/// 窗口背景图：按设置在默认底图、bgskin 目录、自定义路径、网络 URL 之间切换。
/// 目录模式每次启动随机取一张；网络模式先显示默认底图，下载完成后再替换。
/// </summary>
internal static class BackgroundPicker
{
    private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    /// <summary>每次应用递增，防止旧的网络下载结果覆盖新的设置。</summary>
    private static int _applyVersion;

    /// <summary>启动器目录下的 bgskin 路径。</summary>
    public static string FolderPath(string launcherDirectory) => Path.Combine(launcherDirectory, "bgskin");

    /// <summary>确保 bgskin 目录存在；在用户选择目录模式或打开目录时调用。</summary>
    public static void EnsureFolder(string launcherDirectory, ILogger log)
    {
        var folder = FolderPath(launcherDirectory);
        try
        {
            if (Directory.Exists(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            log.Info($"已创建背景图目录：{folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log.Warn($"创建背景图目录失败：{folder}", ex);
        }
    }

    /// <summary>按当前设置应用背景，必须在 UI 线程调用。</summary>
    public static void Apply(
        Window window,
        AppSettings settings,
        string launcherDirectory,
        HttpDownloader downloader,
        ILogger log)
    {
        var version = Interlocked.Increment(ref _applyVersion);

        if (settings.BackgroundMode == BackgroundMode.Network
            && !string.IsNullOrWhiteSpace(settings.BackgroundUrl))
        {
            // 网络图需要等待下载：先铺默认底图，避免窗口开场空白。
            window.Background = CreateBrush(LoadDefault());
            _ = ApplyNetworkAsync(window, settings.BackgroundUrl.Trim(), version, downloader, log);
            return;
        }

        var image = settings.BackgroundMode switch
        {
            BackgroundMode.Folder => LoadRandomFromFolder(FolderPath(launcherDirectory), log) ?? LoadDefault(),
            BackgroundMode.Custom => LoadCustom(settings.BackgroundCustomPath, log),
            _ => LoadDefault(),
        };

        window.Background = CreateBrush(image);
    }

    private static async Task ApplyNetworkAsync(
        Window window,
        string url,
        int version,
        HttpDownloader downloader,
        ILogger log)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var bytes = await downloader.GetBytesAsync(url, cts.Token).ConfigureAwait(false);
            var image = Decode(bytes);
            if (image is null)
            {
                log.Warn($"网络背景图无法解码，保持默认底图：{url}");
                return;
            }

            await window.Dispatcher.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _applyVersion) != version)
                {
                    return;
                }

                window.Background = CreateBrush(image);
                log.Info($"已应用网络背景图：{url}");
            });
        }
        catch (Exception ex)
        {
            log.Warn($"下载网络背景图失败，保持默认底图：{url}", ex);
        }
    }

    /// <summary>自定义路径：目录则随机取图，图片文件则固定使用该文件。</summary>
    private static ImageSource LoadCustom(string? path, ILogger log)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return LoadDefault();
        }

        if (Directory.Exists(path))
        {
            return LoadRandomFromFolder(path, log) ?? LoadDefault();
        }

        if (File.Exists(path) && IsSupported(path))
        {
            return LoadFile(path, log) ?? LoadDefault();
        }

        log.Warn($"自定义背景路径不可用，改用默认底图：{path}");
        return LoadDefault();
    }

    private static ImageSource? LoadRandomFromFolder(string folder, ILogger log)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            var files = Directory.EnumerateFiles(folder)
                .Where(IsSupported)
                .ToList();

            if (files.Count == 0)
            {
                return null;
            }

            var file = files[Random.Shared.Next(files.Count)];
            var image = LoadFile(file, log);
            if (image is not null)
            {
                log.Info($"使用随机背景图：{file}");
            }

            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"扫描背景图目录失败：{folder}", ex);
            return null;
        }
    }

    private static ImageSource? LoadFile(string file, ILogger log)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // 读完立刻释放文件句柄
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or FileFormatException or ArgumentException)
        {
            log.Warn($"读取背景图失败：{file}", ex);
            return null;
        }
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ImageSource LoadDefault()
    {
        var image = new BitmapImage(Skin.BackgroundImage);
        image.Freeze();
        return image;
    }

    private static ImageBrush CreateBrush(ImageSource image) => new(image) { Stretch = Stretch.Fill };
}
