using UiharuMind.Core.AI.Character.PromptActions;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 一次出图请求，与接口格式无关。带输入图即编辑：第 1 张为主图（要改的那张），其余为参考图。
/// </summary>
/// <param name="Prompt">提示词</param>
/// <param name="Images">输入图，空则为生成</param>
/// <param name="AspectRatio">比例；null 时生成按 1:1，编辑跟主图</param>
public sealed record ImageRequest(string Prompt, IReadOnlyList<ImageInput> Images, ImageAspectRatio? AspectRatio)
{
    /// <summary>单次最多几张输入图（各家上限的交集）</summary>
    public const int MaxImages = 5;

    /// <summary>是编辑而非生成</summary>
    public bool IsEdit => Images.Count > 0;
}
