using System;
using Avalonia;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 遮罩暗层的四条带子：围住选区，中间那块洞露出原画面。
/// 用四个矩形而不是 CombinedGeometry 打洞，是因为打洞得每帧新建几何，
/// 丢弃的 SKPath 被终结器释放时合成器还引用着它，命中测试与渲染都会撞上已释放的原生对象。
/// 纯函数，可单测。
/// </summary>
/// <param name="Top">选区上方</param>
/// <param name="Bottom">选区下方</param>
/// <param name="Left">选区左侧</param>
/// <param name="Right">选区右侧</param>
public readonly record struct CaptureDimBands(Rect Top, Rect Bottom, Rect Left, Rect Right)
{
    /// <summary>
    /// 算出围住 <paramref name="hole"/> 的四条带子
    /// </summary>
    /// <param name="window">窗口尺寸（DIP）；非法值按 0 处理</param>
    /// <param name="hole">要露出来的洞（窗内 DIP）；空矩形表示全暗</param>
    /// <returns>四条带子，合起来恰好是窗口减去洞</returns>
    public static CaptureDimBands Calculate(Size window, Rect hole)
    {
        double width = Sanitize(window.Width);
        double height = Sanitize(window.Height);

        // 洞夹到窗口内，免得在边上拖出去时算出负尺寸
        double left = Math.Clamp(Sanitize(hole.X), 0, width);
        double top = Math.Clamp(Sanitize(hole.Y), 0, height);
        double right = Math.Clamp(Sanitize(hole.Right), left, width);
        double bottom = Math.Clamp(Sanitize(hole.Bottom), top, height);

        return new CaptureDimBands(
            new Rect(0, 0, width, top),
            new Rect(0, bottom, width, height - bottom),
            new Rect(0, top, left, bottom - top),
            new Rect(right, top, width - right, bottom - top));
    }

    private static double Sanitize(double value)
    {
        return double.IsNaN(value) || value < 0 ? 0 : value;
    }
}
