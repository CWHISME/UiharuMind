using SkiaSharp;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 钉死「发出去的那份图到底变小了没有」，以及缩放尺寸与编码格式的判定。
/// 尺寸算错会让带图会话的 token 估算(框架按 字节数/4 估)跟着错；
/// 格式错则更隐蔽——只缩尺寸不转 JPEG 的话，照片存成 PNG 仍有 1~2MB，等于没缩。
/// </summary>
public class ConversationImageDownscalerTests
{
    [Fact]
    public void WithinLimit_NeedsNoScaling()
    {
        Assert.Null(ConversationImageDownscaler.ComputeTargetSize(1024, 768, 1568));
        Assert.Null(ConversationImageDownscaler.ComputeTargetSize(1568, 1568, 1568));
    }

    [Fact]
    public void OversizedLandscape_ScalesLongEdgeToLimit()
    {
        (int Width, int Height)? size = ConversationImageDownscaler.ComputeTargetSize(3840, 2160, 1568);

        Assert.NotNull(size);
        Assert.Equal(1568, size!.Value.Width);
        Assert.Equal(882, size.Value.Height); //2160 * 1568 / 3840 = 882
    }

    [Fact]
    public void OversizedPortrait_ScalesByHeight()
    {
        (int Width, int Height)? size = ConversationImageDownscaler.ComputeTargetSize(1080, 3840, 1568);

        Assert.NotNull(size);
        Assert.Equal(1568, size!.Value.Height);
        Assert.Equal(441, size.Value.Width);
    }

    [Fact]
    public void ExtremeAspectRatio_KeepsShortEdgeAtLeastOnePixel()
    {
        (int Width, int Height)? size = ConversationImageDownscaler.ComputeTargetSize(20000, 3, 1568);

        Assert.NotNull(size);
        Assert.True(size!.Value.Height >= 1, "短边为 0 会让 PixelSize 构造抛异常");
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void DegenerateSize_IsRejected(int width, int height)
    {
        Assert.Null(ConversationImageDownscaler.ComputeTargetSize(width, height, 1568));
    }

    [Fact]
    public void SniffsPngJpegAndWebp()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
        byte[] webp = "RIFF"u8.ToArray().Concat(new byte[] { 0, 0, 0, 0 }).Concat("WEBP"u8.ToArray()).ToArray();

        Assert.Equal("image/png", ConversationImageDownscaler.SniffMediaType(png));
        Assert.Equal("image/jpeg", ConversationImageDownscaler.SniffMediaType(jpeg));
        Assert.Equal("image/webp", ConversationImageDownscaler.SniffMediaType(webp));
        Assert.Null(ConversationImageDownscaler.SniffMediaType([1, 2, 3, 4]));
    }

    /// <summary>
    /// 端到端:真造一张大图跑一遍。这条是整件事的支点——「缩了没有」以前只能靠推理，
    /// 而实测发现 Avalonia 的 Bitmap.Save 只写 PNG，照片缩到 1568px 仍有 1~2MB，
    /// 等于没缩。改用 SkiaSharp 之后，这里才真的能钉住。
    /// </summary>
    [Fact]
    public void PhotoLikeImage_ShrinksAndFallsBackToJpeg()
    {
        byte[] original = MakeNoisyPhoto(3840, 2160);

        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale(original, "image/png");

        Assert.Equal("image/jpeg", type); //照片 PNG 压不动,只能退到有损
        Assert.True(bytes.Length < original.Length,
            $"重编码后应更小,实际 {original.Length} → {bytes.Length}");

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.Equal(ConversationImageDownscaler.MaxEdge, decoded.Width);
        Assert.Equal(882, decoded.Height); //2160 * 1568 / 3840
    }

    /// <summary>
    /// 截图这类合成图必须保持无损。JPEG 的 DCT 与色度子采样正好啃高对比度细边缘——
    /// 也就是文字，而截图是本应用的主场景，认错字的代价远大于省下的那点体积。
    /// </summary>
    [Fact]
    public void ScreenshotLikeImage_StaysLossless()
    {
        byte[] original = MakeFlatUiScreenshot(2560, 1440);

        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale(original, "image/png");

        Assert.Equal("image/png", type);

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.Equal(ConversationImageDownscaler.MaxEdge, decoded.Width);
    }

    [Fact]
    public void TransparentAreas_BecomeWhiteNotBlack()
    {
        //JPEG 没有 alpha 通道,不铺白底的话带透明区域的截图会变成黑块
        using SKBitmap source = new(2000, 2000);
        using (SKCanvas canvas = new(source)) canvas.Clear(SKColors.Transparent);
        using SKData png = SKImage.FromBitmap(source).Encode(SKEncodedImageFormat.Png, 100);

        (byte[] bytes, _) = ConversationImageDownscaler.Downscale(png.ToArray(), "image/png");

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        SKColor pixel = decoded.GetPixel(decoded.Width / 2, decoded.Height / 2);
        Assert.True(pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200, $"透明区应铺成白色,实际 {pixel}");
    }

    /// <summary>
    /// 照片走 JPEG 时也得进体积预算。只有 q85 一档的话，一张大照片会以 300~400KB 发出去——
    /// 而图片是随历史每一轮重传的，那些字节要付很多次。
    ///
    /// 素材是特意挑的：这张图 q85=362KB、q70=156KB 都超预算，要一路降到 q55=87KB 才进得去，
    /// 所以它真的会走完整个降质循环。换成压得动的素材，这条测试就是空的。
    /// </summary>
    [Fact]
    public void PhotoLikeImage_FitsTheInlineByteBudget()
    {
        byte[] original = MakeTexturedPhoto(3840, 2160);

        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale(original, "image/png");

        Assert.Equal("image/jpeg", type);
        Assert.True(bytes.Length <= ConversationImageDownscaler.MaxInlineBytes,
            $"应压进 {ConversationImageDownscaler.MaxInlineBytes:N0} 字节预算,实际 {bytes.Length:N0}");
    }

    /// <summary>
    /// 最低档仍超预算时必须照发。发不出图比图大糟得多——那是一次静默的功能缺失。
    /// 纯随机噪声是 JPEG 的最坏输入，真实照片不会这样。
    /// </summary>
    [Fact]
    public void IncompressibleImage_IsStillSentAtTheLowestQuality()
    {
        byte[] original = MakeNoisyPhoto(3840, 2160);

        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale(original, "image/png");

        Assert.Equal("image/jpeg", type);
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length < original.Length, "即使超预算,也总该比原图小");
    }

    /// <summary>
    /// 渐变加较强抖动。PNG 压不动（逼它走有损这条路），JPEG 压得动但要降到最低档才进预算——
    /// 细节丰富的真实照片就是这个量级。抖动幅度是量出来的，改小了这条测试会变空。
    /// </summary>
    private static byte[] MakeTexturedPhoto(int width, int height)
    {
        const int jitter = 60;
        using SKBitmap bitmap = new(width, height);
        Random random = new(4321); //固定种子,测试要可复现
        SKColor[] pixels = new SKColor[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int shade = (x * 255 / width + y * 255 / height) / 2 + random.Next(-jitter, jitter + 1);
                byte channel = (byte)Math.Clamp(shade, 0, 255);
                pixels[y * width + x] = new SKColor(channel, (byte)(255 - channel), channel);
            }
        }

        bitmap.Pixels = pixels;

        using SKData data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>逐像素噪声，PNG 压不动——照片的代理</summary>
    private static byte[] MakeNoisyPhoto(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        Random random = new(1234); //固定种子,测试要可复现
        SKColor[] pixels = new SKColor[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        }

        bitmap.Pixels = pixels; //整块写:逐像素 SetPixel 在 800 万像素上要好几秒

        using SKData data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>大片纯色加几个矩形，PNG 压得极好——截图/UI 的代理</summary>
    private static byte[] MakeFlatUiScreenshot(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(SKColors.White);
            using SKPaint paint = new() { Color = SKColors.DarkSlateBlue };
            canvas.DrawRect(40, 40, width - 80, 120, paint);
            canvas.DrawRect(40, 220, 300, 60, paint);
        }

        using SKData data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void EmptyInput_IsReturnedUntouched()
    {
        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale([], "image/png");

        Assert.Empty(bytes);
        Assert.Equal("image/png", type);
    }

    [Fact]
    public void UndecodableInput_FallsBackToTheOriginal()
    {
        byte[] garbage = [1, 2, 3, 4, 5];

        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale(garbage, "image/png");

        Assert.Equal(garbage, bytes);
        Assert.Equal("image/png", type);
    }
}
