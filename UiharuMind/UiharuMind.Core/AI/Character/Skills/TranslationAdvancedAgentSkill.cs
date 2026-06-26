using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;

namespace UiharuMind.Core.AI.Character.Skills;

public class TranslationAdvancedAgentSkill : AgentSkillConvertableBase
{
    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        TrySetParams("user_request", "None");

        _chatHistory = [new ChatMessageData { Role = ECharacter.User, Content = text }];
        return modelRunningData.InvokeAgentStreamingAsync(GetCharacterData(), _chatHistory, args, cancellationToken);
    }

    /// <summary>
    /// 设置额外需求
    /// </summary>
    /// <param name="extraRequest"></param>
    public void SetExtraRequest(string extraRequest)
    {
        SetParams("user_request", extraRequest);
    }
    
    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.TranslatorAdvanced);
    }
}
