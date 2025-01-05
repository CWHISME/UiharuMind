using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Character.Skills;

public class TranslationAdvancedAgentSkill : AgentSkillConvertableBase
{
    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        TrySetParams("user_request", "None");

        var translator = GetCharacterData().ToAgent(modelRunningData.Kernel, args);

        _chatHistory = new ChatHistory();
        _chatHistory.AddMessage(AuthorRole.User, text);
        return modelRunningData.InvokeAgentStreamingAsync(translator, _chatHistory, cancellationToken);
    }

    /// <summary>
    /// 设置额外需求
    /// </summary>
    /// <param name="extraRequest"></param>
    public void SetExtraRequest(string extraRequest)
    {
        SetParams("user_request", extraRequest);
    }

    protected override ChatHistory GetChatHistory()
    {
        return _chatHistory;
    }

    protected override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.TranslatorAdvanced);
    }
}