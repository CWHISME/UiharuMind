namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 出图走到哪一步
/// </summary>
public enum EImageStage
{
    /// <summary>起引擎、读权重</summary>
    Starting,

    /// <summary>排在别的出图后面</summary>
    Queued,

    /// <summary>在采样</summary>
    Generating
}

/// <summary>
/// 出图进度。本地引擎报步数与预览，远程接口不报
/// </summary>
/// <param name="Stage">阶段</param>
/// <param name="Step">当前步（采样时）</param>
/// <param name="TotalSteps">这一轮共几步</param>
/// <param name="Preview">中间预览图（PNG）；没有为 null</param>
public sealed record ImageProgress(EImageStage Stage, int Step = 0, int TotalSteps = 0, byte[]? Preview = null)
{
    /// <summary>
    /// 起引擎时读权重的进度（0~1，按日志估）；不在起为 0
    /// </summary>
    public float LoadProgress { get; init; }
}
