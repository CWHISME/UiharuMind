using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Core.Tests.AI.ImageGeneration;

public class ImageSizingTests
{
    [Theory]
    [InlineData("16:9", true)]
    [InlineData(" 9 : 16 ", true)]
    [InlineData("21:9", false)] //只有 Agnes 支持，不进交集
    [InlineData("1:1:1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_OnlyAcceptsSupportedRatios(string? text, bool expected)
    {
        Assert.Equal(expected, ImageAspectRatio.TryParse(text, out _));
    }

    [Fact]
    public void Nearest_PicksClosestSupportedRatio()
    {
        Assert.Equal(new ImageAspectRatio(16, 9), ImageAspectRatio.Nearest(1920, 1080));
        Assert.Equal(new ImageAspectRatio(3, 4), ImageAspectRatio.Nearest(1200, 1600));
        Assert.Equal(ImageAspectRatio.Square, ImageAspectRatio.Nearest(1000, 1010));
    }

    [Fact]
    public void Square2K_IsExactly2048()
    {
        Assert.Equal((2048, 2048), ImageSizing.ToPixels(ImageAspectRatio.Square, EImageResolution.Res2K, 32, 512, 4096));
    }

    [Fact]
    public void Widescreen2K_KeepsAreaAndAlignment()
    {
        (int width, int height) = ImageSizing.ToPixels(new ImageAspectRatio(16, 9), EImageResolution.Res2K, 32, 512, 4096);

        // SenseNova 文档的推荐值是 2720x1536，面积不变换算得到的应当与它相差不到一格
        Assert.InRange(width, 2720 - 32, 2720 + 32);
        Assert.InRange(height, 1536 - 32, 1536 + 32);
        Assert.Equal(0, width % 32);
        Assert.Equal(0, height % 32);
    }

    [Fact]
    public void Widescreen4K_ClampsLongEdgeWithoutDistortion()
    {
        (int width, int height) = ImageSizing.ToPixels(new ImageAspectRatio(16, 9), EImageResolution.Res4K, 32, 512, 4096);

        Assert.Equal(4096, width);
        Assert.Equal(2304, height);
    }
}
