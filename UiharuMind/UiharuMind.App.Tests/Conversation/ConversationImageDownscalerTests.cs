using SkiaSharp;
using UiharuMind.Features.Conversation;

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
    /// 等于没缩。改用 SkiaSharp 直接编 JPEG 之后，这里才真的能钉住。
    /// </summary>
    [Fact]
    public void LargePng_ShrinksAndBecomesJpeg()
    {
        byte[] original = MakePng(3840, 2160);

        (byte[] bytes, string type) = ConversationImageDownscaler.Downscale(original, "image/png");

        Assert.Equal("image/jpeg", type);
        Assert.True(bytes.Length < original.Length,
            $"重编码后应更小,实际 {original.Length} → {bytes.Length}");

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.Equal(ConversationImageDownscaler.MaxEdge, decoded.Width);
        Assert.Equal(882, decoded.Height); //2160 * 1568 / 3840
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

    private static byte[] MakePng(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(SKColors.White);
            //画点噪声,免得整块纯色被 PNG 压到极小、显不出体积差异
            using SKPaint paint = new() { Color = SKColors.DarkSlateBlue };
            for (int i = 0; i < width; i += 7)
            {
                canvas.DrawRect(i, i % height, 5, 40, paint);
            }
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
