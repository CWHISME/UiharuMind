using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// 允许转为临时对话
/// </summary>
public abstract class AgentSkillConvertableBase : AgentSkillBase
{
    protected List<ChatMessageData>? _chatHistory;

    public override bool IsConvertableToChatSession => _chatHistory != null;

    public override ChatSession? TryConvertToChatSession()
    {
        var chatHistory = GetChatHistory();
        // if (chatHistory == null) return null;

        var characterData = GetCharacterData();
        var chatSession = new ChatSession(characterData.CharacterName, characterData);
        if (chatHistory.Count > 0) chatSession.Description = chatHistory[0].Content;
        chatSession.ReInitHistory(chatHistory);
        chatSession.ChatModelRunningData = CurModelRunningData;
        chatSession.IsDirty = true;
        return chatSession;
    }

    protected virtual List<ChatMessageData> GetChatHistory()
    {
        return _chatHistory ?? [];
    }

    public abstract CharacterData GetCharacterData();

    public override string ToString()
    {
        return GetCharacterData().CharacterName;
    }
}
