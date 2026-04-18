using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;

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
        var translator = GetCharacterData().ToAgent(modelRunningData.Kernel, args);

        _chatHistory = new ChatHistory();
        _chatHistory.AddMessage(AuthorRole.User, text);
        return modelRunningData.InvokeAgentStreamingAsync(translator, _chatHistory, cancellationToken);
    }

    public override CharacterData GetCharacterData()
    {
        return _characterData;
    }
}