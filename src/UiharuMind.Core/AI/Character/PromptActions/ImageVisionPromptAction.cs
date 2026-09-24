using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 外部提供图片询问（支持多张同框，用于对比等场景）
/// </summary>
public class ImageVisionPromptAction : PromptActionVisionBase
{

    public ImageVisionPromptAction(IReadOnlyList<ImageInput> images) : base(images)
    {
    }

    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.ImageVisionPrompt);
    }
}
