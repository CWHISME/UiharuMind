using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// ocr agent skill
/// </summary>
public class ImageOcrPromptAction : PromptActionVisionBase
{
    
    public ImageOcrPromptAction(byte[] imageBytes) : base(imageBytes)
    {
    }

    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.VisionOcr);
    }
}
