using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;

namespace UiharuMind.Core.AI.Character.Skills;

public class TranslationAgentSkill : AgentSkillConvertableBase
{
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
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Translator);
    }
}
