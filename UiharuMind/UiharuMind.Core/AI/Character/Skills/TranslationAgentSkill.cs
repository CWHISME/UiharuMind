using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Character.Skills;

public class TranslationAgentSkill : AgentSkillConvertableBase
{
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
    
    protected override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Translator);
    }
}