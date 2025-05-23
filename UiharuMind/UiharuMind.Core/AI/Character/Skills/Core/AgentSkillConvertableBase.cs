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
    protected ChatHistory? _chatHistory;

    public override bool IsConvertableToChatSession => _chatHistory != null;

    public override ChatSession? TryConvertToChatSession()
    {
        var chatHistory = GetChatHistory();
        // if (chatHistory == null) return null;

        var characterData = GetCharacterData();
        var chatSession = new ChatSession(characterData.CharacterName, characterData);
        if (chatHistory.Count > 0) chatSession.Description = chatHistory[0].Content ?? "临时对话";
        chatSession.ReInitHistory(chatHistory);
        chatSession.ChatModelRunningData = CurModelRunningData;
        chatSession.IsDirty = true;
        return chatSession;
    }

    protected virtual ChatHistory GetChatHistory()
    {
        return _chatHistory ?? new ChatHistory();
    }

    protected abstract CharacterData GetCharacterData();
}