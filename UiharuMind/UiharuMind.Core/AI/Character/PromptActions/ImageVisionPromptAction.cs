using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 外部提供图片询问
/// </summary>
public class ImageVisionPromptAction : PromptActionVisionBase
{
    
    public ImageVisionPromptAction(byte[] imageBytes) : base(imageBytes)
    {
    }
    
    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Vision);
    }
}
