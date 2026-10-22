using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MCLauncher.App;

/// <summary>
/// 把 64×64 / 64×32 皮肤纹理拼成正面角色预览，并把披风纹理裁成背面图。
/// 只画正面的六块，够用于确认换肤结果；渲染结果按整数倍放大，保持像素风。
/// </summary>
public static class SkinPreviewRenderer
{
    private const int Scale = 5;

    /// <summary>把 PNG 字节解码成可跨线程使用的纹理。</summary>
    public static BitmapSource? LoadTexture(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>皮肤必须是 64×64 或旧版 64×32。</summary>
    public static bool IsValidSkinSize(BitmapSource texture) =>
        texture.PixelWidth == 64 && texture.PixelHeight is 64 or 32;

    /// <summary>标准判定：64×64 皮肤 (54,20) 透明即为纤细模型。</summary>
    public static bool DetectSlim(BitmapSource texture)
    {
        if (texture.PixelWidth < 64 || texture.PixelHeight < 64)
        {
            return false;
        }

        var converted = new FormatConvertedBitmap(texture, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(54, 20, 1, 1), pixel, 4, 0);
        return pixel[3] == 0;
    }

    /// <summary>正面角色图：经典 80×160，纤细 70×160（每像素格 5 像素）。</summary>
    public static BitmapSource? DrawCharacter(BitmapSource texture, bool slim)
    {
        if (!IsValidSkinSize(texture))
        {
            return null;
        }

        var hasOverlay = texture.PixelHeight >= 64;
        var arm = slim ? 3 : 4;
        var bodyX = arm;
        var width = (8 + arm * 2) * Scale;
        const int height = 32 * Scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var dc = visual.RenderOpen())
        {
            // 腿（旧版 64×32 没有左腿，直接复用右腿）
            Blit(dc, texture, new Int32Rect(4, 20, 4, 12), (bodyX + 4) * Scale, 20 * Scale);
            if (hasOverlay)
            {
                Blit(dc, texture, new Int32Rect(20, 52, 4, 12), bodyX * Scale, 20 * Scale);
            }
            else
            {
                Blit(dc, texture, new Int32Rect(4, 20, 4, 12), bodyX * Scale, 20 * Scale);
            }

            // 身体
            Blit(dc, texture, new Int32Rect(20, 20, 8, 12), bodyX * Scale, 8 * Scale);

            // 手臂（左臂在旧版里同样复用右臂）
            Blit(dc, texture, new Int32Rect(44, 20, arm, 12), (bodyX + 8) * Scale, 8 * Scale);
            if (hasOverlay)
            {
                Blit(dc, texture, new Int32Rect(36, 52, arm, 12), 0, 8 * Scale);
            }
            else
            {
                Blit(dc, texture, new Int32Rect(44, 20, arm, 12), 0, 8 * Scale);
            }

            // 头
            Blit(dc, texture, new Int32Rect(8, 8, 8, 8), bodyX * Scale, 0);

            if (hasOverlay)
            {
                // 外层（衣服、袖子、裤腿、帽子），按 alpha 叠在底图上。
                Blit(dc, texture, new Int32Rect(20, 36, 8, 12), bodyX * Scale, 8 * Scale);
                Blit(dc, texture, new Int32Rect(44, 36, arm, 12), (bodyX + 8) * Scale, 8 * Scale);
                Blit(dc, texture, new Int32Rect(52, 52, arm, 12), 0, 8 * Scale);
                Blit(dc, texture, new Int32Rect(4, 36, 4, 12), (bodyX + 4) * Scale, 20 * Scale);
                Blit(dc, texture, new Int32Rect(4, 52, 4, 12), bodyX * Scale, 20 * Scale);
                Blit(dc, texture, new Int32Rect(40, 8, 8, 8), bodyX * Scale, 0);
            }
        }

        return Render(visual, width, height);
    }

    /// <summary>披风背面：纹理里的 (1,1) 10×16 区域。</summary>
    public static BitmapSource? DrawCape(BitmapSource texture, int width, int height)
    {
        if (texture.PixelWidth < 12 || texture.PixelHeight < 18 || width <= 0 || height <= 0)
        {
            return null;
        }

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var dc = visual.RenderOpen())
        {
            var cape = new CroppedBitmap(texture, new Int32Rect(1, 1, 10, 16));
            dc.DrawImage(cape, new Rect(0, 0, width, height));
        }

        return Render(visual, width, height);
    }

    private static void Blit(
        DrawingContext dc,
        BitmapSource texture,
        Int32Rect source,
        int x,
        int y)
    {
        var crop = new CroppedBitmap(texture, source);
        dc.DrawImage(crop, new Rect(x, y, source.Width * Scale, source.Height * Scale));
    }

    private static BitmapSource Render(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
