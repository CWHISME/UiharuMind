using SkiaSharp;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Features.Desktop;

namespace UiharuMind.App.Tests.Desktop;

/// <summary>
/// 桌面扩展的看图（ADR 0084）：认字走截图 OCR 那一套，画编号框后缩到适合交给模型的大小
/// </summary>
[Collection(HeadlessCollection.Name)]
public class DesktopVisionTests
{
    // 解码位图要 Avalonia 的渲染层：放进无头界面里跑
    [Fact]
    public void ReadsTheTextInAPicture_WithBoxesInsideThePicture()
    {
        if (!OperatingSystem.IsMacOS()) return; //目前只有 macOS 有系统 OCR

        HeadlessUi.RunAsync(async () =>
        {
            IReadOnlyList<DesktopTextLine> lines = await new DesktopVision().RecognizeAsync(Picture(800, 400, "Start Game"),
                CancellationToken.None);

            DesktopTextLine line = Assert.Single(lines);
            Assert.Equal("Start Game", line.Text);
            Assert.InRange(line.Box.X, 0, 1);
            Assert.InRange(line.Box.Y, 0, 1);
        });
    }

    [Fact]
    public void Marking_ShrinksLargePicturesToAJpeg()
    {
        byte[] jpeg = new DesktopVision().Mark(Picture(3200, 1600, "x"), [new DesktopMark("e1", new AxRect(0.1, 0.1, 0.2, 0.1))]);

        using SKBitmap marked = SKBitmap.Decode(jpeg);
        Assert.Equal((1600, 800), (marked.Width, marked.Height));
        Assert.Equal(0xFF, jpeg[0]); //JPEG 头
    }

    private static byte[] Picture(int width, int height, string text)
    {
        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.White);
        using SKFont font = new(SKTypeface.Default, 48);
        using SKPaint ink = new() { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 100, 200, SKTextAlign.Left, font, ink);
        using SKImage image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
