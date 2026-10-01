using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

public class ImageFormatsTests
{
    [Fact]
    public void SniffsPngJpegAndWebp()
    {
        Assert.Equal("image/png", ImageFormats.Sniff(TestImages.Png(1, 1)));
        Assert.Equal("image/jpeg", ImageFormats.Sniff(TestImages.Jpeg(1, 1)));
        Assert.Equal("image/webp", ImageFormats.Sniff(TestImages.WebpExtended(1, 1)));
        Assert.Null(ImageFormats.Sniff([1, 2, 3, 4]));
    }

    [Fact]
    public void ReadsSizeFromHeaders()
    {
        AssertSize(TestImages.Png(1920, 1080), 1920, 1080);
        AssertSize(TestImages.Jpeg(800, 1200), 800, 1200);
        AssertSize(TestImages.WebpExtended(3000, 2000), 3000, 2000);
    }

    [Fact]
    public void UnknownBytes_HaveNoSize()
    {
        Assert.False(ImageFormats.TryReadSize([0xFF, 0xD8, 0xFF, 0xE0], out _, out _));
        Assert.False(ImageFormats.TryReadSize([1, 2, 3], out _, out _));
    }

    [Theory]
    [InlineData("a.PNG", "image/png")]
    [InlineData("b.jpeg", "image/jpeg")]
    [InlineData("c.webp", "image/webp")]
    [InlineData("d.txt", "fallback")]
    public void MediaTypeFromPath_UsesFallbackForUnknown(string path, string expected)
    {
        Assert.Equal(expected, ImageFormats.MediaTypeFromPath(path, "fallback"));
    }

    [Fact]
    public void DataUrl_CarriesFullPrefix()
    {
        Assert.Equal("data:image/png;base64,AQI=", ImageFormats.ToDataUrl([1, 2], "image/png"));
    }

    private static void AssertSize(byte[] bytes, int width, int height)
    {
        Assert.True(ImageFormats.TryReadSize(bytes, out int actualWidth, out int actualHeight));
        Assert.Equal((width, height), (actualWidth, actualHeight));
    }
}
