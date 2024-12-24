using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// 允许转为临时对话
/// </summary>
public abstract class AgentSkillConvertableBase : AgentSkillBase
{
    protected ChatHistory _chatHistory;

    public override bool IsConvertableToChatSession => true;

    public override ChatSession TryConvertToChatSession()
    {
        var characterData = GetCharacterData();
        var chatSession = new ChatSession(characterData.CharacterName, characterData);
        chatSession.ReInitHistory(GetChatHistory());
        chatSession.ChatModelRunningData = CurModelRunningData;
        return chatSession;
    }

    protected abstract ChatHistory GetChatHistory();
    protected abstract CharacterData GetCharacterData();
}