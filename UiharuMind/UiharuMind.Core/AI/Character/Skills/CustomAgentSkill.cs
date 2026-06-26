using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// 自定义角色技能
/// </summary>
public class CustomAgentSkill : AgentSkillConvertableBase
{
    private CharacterData _characterData;

    public CustomAgentSkill(CharacterData characterData)
    {
        _characterData = characterData;
    }

    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        // AddParams("content", text);
        _chatHistory = [new ChatMessageData { Role = ECharacter.User, Content = text }];
        return modelRunningData.InvokeAgentStreamingAsync(GetCharacterData(), _chatHistory, args, cancellationToken);
    }

    public override CharacterData GetCharacterData()
    {
        return _characterData;
    }
}
