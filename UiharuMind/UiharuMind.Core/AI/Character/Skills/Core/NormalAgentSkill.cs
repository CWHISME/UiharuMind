using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

public class NormalAgentSkill : AgentSkillConvertableBase
{
    protected CharacterData _characterData;

    public NormalAgentSkill(CharacterData characterData)
    {
        _characterData = characterData;
    }

    public NormalAgentSkill(DefaultCharacter character) : this(
        DefaultCharacterManager.Instance.GetCharacterData(character))
    {
    }

    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData? modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        // AddParams("content", text);
        var agent = GetCharacterData().ToAgent(modelRunningData.Kernel, args);

        _chatHistory = new ChatHistory();
        _chatHistory.AddMessage(AuthorRole.User, text);
        return modelRunningData.InvokeAgentStreamingAsync(agent, _chatHistory, cancellationToken);
    }

    protected override ChatHistory? GetChatHistory()
    {
        return _chatHistory;
    }

    protected override CharacterData GetCharacterData()
    {
        return _characterData; //DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.AssistantExpert);
    }
}