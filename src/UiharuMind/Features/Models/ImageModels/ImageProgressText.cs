using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 出图进度怎么说给用户听（画图窗口与智能体的生图工具卡同一套说法）
/// </summary>
public static class ImageProgressText
{
    /// <summary>
    /// 进度的一句话
    /// </summary>
    /// <param name="progress">进度</param>
    /// <returns>说明；采样中还不知道总步数时为 null，沿用上一句</returns>
    public static string? Describe(ImageProgress progress) => progress.Stage switch
    {
        EImageStage.Starting => Loc.Text(LangKey.ImageDrawStarting),
        EImageStage.Queued => Loc.Text(LangKey.ImageDrawQueued),
        _ when progress.TotalSteps > 0 => Loc.Text(LangKey.ImageDrawStep, progress.Step, progress.TotalSteps),
        _ => null
    };
}
