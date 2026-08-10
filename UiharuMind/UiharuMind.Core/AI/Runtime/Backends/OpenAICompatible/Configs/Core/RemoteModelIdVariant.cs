namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// 单个 ModelId 的预设:默认上下文大小与是否支持视觉
/// </summary>
/// <param name="ContextLength">默认上下文窗口(token 数),0 表示未设置</param>
/// <param name="IsVision">是否支持视觉</param>
public record RemoteModelIdVariant(int ContextLength = 0, bool IsVision = false)
{
    /// <summary>
    /// 空表,供未声明预设的配置使用
    /// </summary>
    public static IReadOnlyDictionary<string, RemoteModelIdVariant> Empty { get; } =
        new Dictionary<string, RemoteModelIdVariant>();
}