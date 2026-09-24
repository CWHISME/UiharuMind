using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// OCR 识图技能（单图）
/// </summary>
public class ImageOcrPromptAction : PromptActionVisionBase
{
    
    public ImageOcrPromptAction(ImageInput image) : base([image])
    {
    }

    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.ImageOcrPrompt);
    }
}
