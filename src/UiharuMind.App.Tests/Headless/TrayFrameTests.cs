using SkiaSharp;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Spinner;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 托盘帧：两个平台共用一张素图，Idle 静态、Running 绕一圈、AwaitingApproval 摆一个来回。
/// Windows 靠染色解决「黑剪影落在深色任务栏上看不见」，染色只换墨色、不动形状。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class TrayFrameTests
{
    /// <summary>帧数与动画循环对得上：转一圈取 <see cref="SpinClock.FrameCount"/>，摆动取来回帧数</summary>
    [Fact]
    public void Build_MakesOneIdle_AndAFullRoundOfEachAnimation() => HeadlessUi.Run(() =>
    {
        using SKBitmap flower = SkiaBitmapUtils.DecodeAsset("TrayFlowerIdle.png");

        TrayFrameBuilder.Frames frames = TrayFrameBuilder.Build(flower);

        Assert.NotNull(frames.Idle);
        Assert.Equal(SpinClock.FrameCount, frames.Running.Length);
        Assert.Equal(TrayFrameBuilder.WobbleFrameCount, frames.Alert.Length);
    });

    /// <summary>染色只换墨色：轮廓（alpha 总和）不变，实心处变成目标色，留白仍透明</summary>
    [Fact]
    public void Tint_ReplacesTheInk_AndKeepsTheShape() => HeadlessUi.Run(() =>
    {
        using SKBitmap flower = SkiaBitmapUtils.DecodeAsset("TrayFlowerIdle.png");

        using SKBitmap white = SkiaBitmapUtils.Tint(flower, SKColors.White);

        Assert.Equal(flower.Width, white.Width);
        Assert.Equal(flower.Height, white.Height);
        Assert.Equal(AlphaSum(flower), AlphaSum(white));

        SKColor solid = InkOf(white);
        Assert.Equal(255, solid.Alpha);
        Assert.Equal((byte)255, solid.Red);
        Assert.Equal((byte)255, solid.Green);
        Assert.Equal((byte)255, solid.Blue);
        Assert.Equal(0, white.GetPixel(0, 0).Alpha); //花心与四角本来就透明
    });

    private static long AlphaSum(SKBitmap bitmap)
    {
        long sum = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            sum += bitmap.GetPixel(x, y).Alpha;
        return sum;
    }

    private static SKColor InkOf(SKBitmap bitmap)
    {
        SKColor ink = SKColors.Transparent;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            SKColor pixel = bitmap.GetPixel(x, y);
            if (pixel.Alpha > ink.Alpha) ink = pixel;
        }

        return ink;
    }
}
