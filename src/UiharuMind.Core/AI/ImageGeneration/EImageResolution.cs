namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 分辨率档：长宽乘积约为档位边长的平方（2K ≈ 2048²）。
/// 决定价钱，所以是生图模型的设置，不交给对话模型选。
/// </summary>
public enum EImageResolution
{
    Res1K = 1,
    Res2K = 2,
    Res3K = 3,
    Res4K = 4,
}

/// <summary>分辨率档的换算</summary>
public static class ImageResolutionExtensions
{
    /// <summary>
    /// 档位标签
    /// </summary>
    /// <param name="resolution">分辨率档</param>
    /// <returns>形如 <c>2K</c></returns>
    public static string ToTierLabel(this EImageResolution resolution) => $"{(int)resolution}K";

    /// <summary>
    /// 档位对应的方图边长
    /// </summary>
    /// <param name="resolution">分辨率档</param>
    /// <returns>像素</returns>
    public static int SquareEdge(this EImageResolution resolution) => (int)resolution * 1024;
}
