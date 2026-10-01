namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 比例 + 分辨率档 → 像素宽高。给要求写死 <c>宽x高</c> 的接口格式用。
/// </summary>
public static class ImageSizing
{
    /// <summary>
    /// 按面积不变换算宽高，再对齐到倍数并夹进上下限
    /// </summary>
    /// <param name="ratio">比例</param>
    /// <param name="resolution">分辨率档</param>
    /// <param name="multiple">宽高须是它的倍数</param>
    /// <param name="minEdge">单边下限</param>
    /// <param name="maxEdge">单边上限</param>
    /// <returns>宽高</returns>
    public static (int Width, int Height) ToPixels(ImageAspectRatio ratio, EImageResolution resolution,
        int multiple, int minEdge, int maxEdge)
    {
        double area = Math.Pow(resolution.SquareEdge(), 2);
        double width = Math.Sqrt(area * ratio.Value);
        double height = width / ratio.Value;

        // 长边超限时整体缩，比例不走样
        double longest = Math.Max(width, height);
        if (longest > maxEdge)
        {
            width *= maxEdge / longest;
            height *= maxEdge / longest;
        }

        return (Align(width, multiple, minEdge, maxEdge), Align(height, multiple, minEdge, maxEdge));
    }

    private static int Align(double value, int multiple, int minEdge, int maxEdge)
    {
        int aligned = (int)Math.Round(value / multiple) * multiple;
        return Math.Clamp(aligned, minEdge, maxEdge);
    }
}
