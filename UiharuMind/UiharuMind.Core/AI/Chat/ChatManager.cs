/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Core.Chat;

public class ChatManager : Singleton<ChatManager>
{
    public readonly List<ChatSession> ChatSessions = LoadChatHistory();

    public event Action<ChatSession>? OnChatSessionAdded;
    public event Action<ChatSession>? OnChatSessionRemoved;

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

    /// <summary>
    ///  开始新的聊天
    /// </summary>
    /// <param name="characterData"></param>
    /// <returns></returns>
    public ChatSession StartNewSession(CharacterData characterData)
    {
        var session = new ChatSession(GetUniqueSessionName(characterData.CharacterName), characterData);
        ChatSessions.Insert(0, session);
        SaveChat(session);
        OnChatSessionAdded?.Invoke(session);
        return session;
    }

    /// <summary>
    /// 删除聊天记录
    /// </summary>
    /// <param name="session"></param>
    public void DeleteSession(ChatSession session)
    {
        ChatSessions.Remove(session);
        DeleteChat(session);
        OnChatSessionRemoved?.Invoke(session);
    }

    /// <summary>
    /// 修改对话名称
    /// </summary>
    /// <param name="session"></param>
    /// <param name="newName"></param>
    public string ModifySessionName(ChatSession session, string newName)
    {
        DeleteChat(session);
        session.Name = GetUniqueSessionName(newName);
        SaveChat(session);
        return session.Name;
    }

    /// <summary>
    /// 描述修改
    /// </summary>
    /// <param name="session"></param>
    /// <param name="newDescription"></param>
    public string ModifySessionDescription(ChatSession session, string newDescription)
    {
        session.Description = newDescription;
        SaveChat(session);
        return session.Description;
    }

    /// <summary>
    /// 保存聊天记录
    /// </summary>
    public static void SaveChat(ChatSession chatSession)
    {
        // if (!Directory.Exists(SettingConfig.SaveChatDataPath))
        //     Directory.CreateDirectory(SettingConfig.SaveChatDataPath);
        string fileName = $"{chatSession.Name}.json";
        //chatSession.LastTime.ToString("yyyy_MM_dd_HHmmss") + ".json";
        SaveUtility.Save(Path.Combine(SettingConfig.SaveChatDataPath, fileName), chatSession);
    }

    /// <summary>
    /// 删除聊天记录
    /// </summary>
    public static void DeleteChat(ChatSession chatSession)
    {
        string fileName = $"{chatSession.Name}.json";
        SaveUtility.Delete(Path.Combine(SettingConfig.SaveChatDataPath, fileName));
    }

    /// <summary>
    /// 重新加载所有聊天记录
    /// </summary>
    /// <returns></returns>
    public static List<ChatSession> LoadChatHistory()
    {
        List<ChatSession> chatSessions = new List<ChatSession>();
        if (!Directory.Exists(SettingConfig.SaveChatDataPath)) return chatSessions;
        foreach (string file in Directory.GetFiles(SettingConfig.SaveChatDataPath, "*.json"))
        {
            var chatSession = SaveUtility.Load<ChatSession>(file);
            if (chatSession != null) chatSessions.Add(chatSession);
        }

        chatSessions.Sort((x, y) => y.LastTime.CompareTo(x.LastTime));
        return chatSessions;
    }

    private string GetUniqueSessionName(string targetName)
    {
        int i = 0;
        string sessionName = targetName;
        // 防止重复
        while (ChatSessions.Exists(x => x.Name == sessionName))
        {
            sessionName = targetName + "_" + i++;
        }

        return sessionName;
    }
}