using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// ocr agent skill
/// </summary>
public class ImageOcrSkill : AgentSkillVisionBase
{
    
    public ImageOcrSkill(byte[] imageBytes) : base(imageBytes)
    {
    }

    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.VisionOcr);
    }
}