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
/// 目录模式每次启动随机取一张；网络模式支持启动页预加载，避免主窗口先默认后换图。
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
    /// <param name="preloadedNetworkImage">启动页已下载好的网络图；有值时直接使用，不再重下。</param>
    /// <param name="allowNetworkLoad">没有预加载图时是否仍发起后台下载；用户跳过启动页时传 false。</param>
    public static void Apply(
        Window window,
        AppSettings settings,
        string launcherDirectory,
        HttpDownloader downloader,
        ILogger log,
        ImageSource? preloadedNetworkImage = null,
        bool allowNetworkLoad = true)
    {
        var version = Interlocked.Increment(ref _applyVersion);

        if (settings.BackgroundMode == BackgroundMode.Network
            && !string.IsNullOrWhiteSpace(settings.BackgroundUrl))
        {
            if (preloadedNetworkImage is not null)
            {
                window.Background = CreateBrush(preloadedNetworkImage);
                return;
            }

            // 网络图需要等待下载：先铺默认底图，避免窗口开场空白。
            window.Background = CreateBrush(LoadDefault());

            if (allowNetworkLoad)
            {
                _ = ApplyNetworkAsync(window, settings.BackgroundUrl.Trim(), version, downloader, log);
            }

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

    /// <summary>启动页期间预取网络背景图；非网络模式、跳过或失败时返回 null。</summary>
    public static async Task<ImageSource?> PreloadNetworkAsync(
        AppSettings settings,
        HttpDownloader downloader,
        ILogger log,
        CancellationToken ct)
    {
        if (settings.BackgroundMode != BackgroundMode.Network
            || string.IsNullOrWhiteSpace(settings.BackgroundUrl))
        {
            return null;
        }

        var url = settings.BackgroundUrl.Trim();

        try
        {
            var bytes = await downloader.GetBytesAsync(url, ct).ConfigureAwait(false);
            var image = Decode(bytes);
            if (image is null)
            {
                log.Warn($"网络背景图无法解码，改用默认底图：{url}");
                return null;
            }

            log.Info($"已预加载网络背景图：{url}");
            return image;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            log.Warn($"预加载网络背景图失败，改用默认底图：{url}", ex);
            return null;
        }
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
