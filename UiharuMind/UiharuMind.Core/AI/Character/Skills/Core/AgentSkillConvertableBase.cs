using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// 允许转为临时对话
/// </summary>
public abstract class AgentSkillConvertableBase : AgentSkillBase
{
    protected List<ChatMessage>? _chatHistory;

    public override bool IsConvertableToChatSession => _chatHistory != null;

    public override ChatSession? TryConvertToChatSession()
    {
        var chatHistory = GetChatHistory();
        // if (chatHistory == null) return null;

        var characterData = GetCharacterData();
        var chatSession = new ChatSession(characterData.CharacterName, characterData);
        if (chatHistory.Count > 0) chatSession.Description = chatHistory[0].Text;
        chatSession.ReInitHistory(chatHistory);
        chatSession.ChatModelRunningData = CurModelRunningData;
        // 临时会话:不落盘、不进索引;用户选择保留时经 ChatSession.Persist 提升
        chatSession.IsTransient = true;
        return chatSession;
    }

    protected virtual List<ChatMessage> GetChatHistory()
    {
        return _chatHistory ?? [];
    }

    public abstract CharacterData GetCharacterData();

    public override string ToString()
    {
        return GetCharacterData().CharacterName;
    }
}
