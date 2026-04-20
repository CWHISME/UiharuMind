using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// 自定义角色的图片对话
/// </summary>
public class CustomImageSkill : AgentSkillVisionBase
{
    private CharacterData _characterData;

    public CustomImageSkill(DefaultCharacter character, byte[] imageBytes) : base(imageBytes)
    {
        _characterData = DefaultCharacterManager.Instance.GetCharacterData(character);
    }

    public CustomImageSkill(CharacterData characterData, byte[] imageBytes) : base(imageBytes)
    {
        _characterData = characterData;
    }

    public override CharacterData GetCharacterData()
    {
        return _characterData;
    }
}