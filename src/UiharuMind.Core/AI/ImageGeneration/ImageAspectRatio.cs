namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 出图比例。只认 <see cref="Supported"/> 里那几种——各家都支持的交集，
/// 模型侧不必知道哪家多支持一个 21:9。
/// </summary>
/// <param name="Width">宽的份数</param>
/// <param name="Height">高的份数</param>
public readonly record struct ImageAspectRatio(int Width, int Height)
{
    /// <summary>1:1</summary>
    public static ImageAspectRatio Square { get; } = new(1, 1);

    /// <summary>各家都支持的比例</summary>
    public static IReadOnlyList<ImageAspectRatio> Supported { get; } =
    [
        Square, new(3, 4), new(4, 3), new(16, 9), new(9, 16), new(2, 3), new(3, 2),
    ];

    /// <summary>宽 ÷ 高</summary>
    public double Value => (double)Width / Height;

    /// <summary>横图（宽大于高）</summary>
    public bool IsLandscape => Width > Height;

    /// <summary>竖图（高大于宽）</summary>
    public bool IsPortrait => Height > Width;

    /// <summary>
    /// 解析 <c>16:9</c> 形式，只认 <see cref="Supported"/>
    /// </summary>
    /// <param name="text">比例文本</param>
    /// <param name="ratio">解析结果</param>
    /// <returns>是受支持的比例返回 true</returns>
    public static bool TryParse(string? text, out ImageAspectRatio ratio)
    {
        ratio = default;
        string[] parts = (text ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int width) || !int.TryParse(parts[1], out int height))
        {
            return false;
        }

        ImageAspectRatio parsed = new(width, height);
        if (!Supported.Contains(parsed)) return false;

        ratio = parsed;
        return true;
    }

    /// <summary>
    /// 取最接近给定宽高的受支持比例
    /// </summary>
    /// <param name="width">宽（像素）</param>
    /// <param name="height">高（像素）</param>
    /// <returns>最接近的比例</returns>
    public static ImageAspectRatio Nearest(int width, int height)
    {
        // 比对数距离：3:2 与 2:3 离 1:1 一样远，不受横竖影响
        double target = Math.Log((double)width / height);
        return Supported.MinBy(r => Math.Abs(Math.Log(r.Value) - target));
    }

    /// <summary>形如 <c>16:9</c></summary>
    public override string ToString() => $"{Width}:{Height}";
}
