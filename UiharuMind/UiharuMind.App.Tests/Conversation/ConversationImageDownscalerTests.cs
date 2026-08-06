using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 钉死缩放尺寸的计算与编码格式的判定。
/// 尺寸算错会让带图会话的 token 估算(框架按 字节数/4 估)跟着错；
/// MIME 标错则会被模型接口直接拒收——Avalonia 的 Save 输出什么格式由后端决定，
/// 因此只能从编码头判定，不能想当然。
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
