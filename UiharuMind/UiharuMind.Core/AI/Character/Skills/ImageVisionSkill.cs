using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// 外部提供图片询问
/// </summary>
public class ImageVisionSkill : AgentSkillVisionBase
{
    
    public ImageVisionSkill(byte[] imageBytes) : base(imageBytes)
    {
    }
    
    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Vision);
    }
}