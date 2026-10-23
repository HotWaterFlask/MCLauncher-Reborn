using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCLauncher.App.Themes;
using MCLauncher.Core.Abstractions;

namespace MCLauncher.App;

/// <summary>
/// 背景图选择：启动器目录下的 <c>bgskin</c> 文件夹里有图片时随机取一张铺满整窗，
/// 没有该目录或目录里没有图片时回退到内置默认底图。
/// </summary>
internal static class BackgroundPicker
{
    private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    public static ImageSource Pick(string launcherDirectory, ILogger log)
    {
        var file = PickRandomFile(Path.Combine(launcherDirectory, "bgskin"));
        if (file is null)
        {
            return LoadDefault();
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // 读完立刻释放文件句柄
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            log.Info($"使用 bgskin 背景图：{file}");
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or FileFormatException)
        {
            log.Warn($"读取背景图失败，改用默认底图：{file}", ex);
            return LoadDefault();
        }
    }

    private static ImageSource LoadDefault()
    {
        var image = new BitmapImage(Skin.BackgroundImage);
        image.Freeze();
        return image;
    }

    private static string? PickRandomFile(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            var files = Directory.EnumerateFiles(folder)
                .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            return files.Count == 0 ? null : files[Random.Shared.Next(files.Count)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
