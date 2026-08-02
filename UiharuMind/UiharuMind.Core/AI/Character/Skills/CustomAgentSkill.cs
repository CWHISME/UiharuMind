using Microsoft.Extensions.AI;
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
        //TODO：存在问题，此处未保存 AI 回复，会导致选择转换对话丢失
        return RunTransientAsync(text, args, cancellationToken: cancellationToken);
    }

    public override CharacterData GetCharacterData()
    {
        return _characterData;
    }
}
