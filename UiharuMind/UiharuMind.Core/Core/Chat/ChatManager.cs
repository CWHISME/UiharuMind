using System.Text.Json.Serialization;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Core.Chat;

public class ChatManager : Singleton<ChatManager>
{
    public List<ChatSession> ChatSessions = SaveUtility.LoadChatHistory();

    /// <summary>
    /// 一般不需要调，因为是单个存储的
    /// </summary>
    public void Save()
    {
        foreach (var session in ChatSessions)
        {
            session.Save();
        }
    }
}