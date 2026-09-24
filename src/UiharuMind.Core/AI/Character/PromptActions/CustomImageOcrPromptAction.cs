using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 自定义角色的图片对话（OCR / 解释等快捷识图，单图）
/// </summary>
public class CustomImageSkill : PromptActionVisionBase
{
    private CharacterData _characterData;

    public CustomImageSkill(DefaultCharacter character, ImageInput image) : base([image])
    {
        _characterData = DefaultCharacterManager.Instance.GetCharacterData(character);
    }

    public CustomImageSkill(CharacterData characterData, ImageInput image) : base([image])
    {
        _characterData = characterData;
    }

    public override CharacterData GetCharacterData()
    {
        return _characterData;
    }
}
