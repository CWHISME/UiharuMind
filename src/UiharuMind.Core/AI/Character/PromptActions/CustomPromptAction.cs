using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 自定义角色技能
/// </summary>
public class CustomPromptAction : PromptActionConvertableBase
{
    private CharacterData _characterData;

    public CustomPromptAction(CharacterData characterData)
    {
        _characterData = characterData;
    }

    protected override IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string text,
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
