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
using UiharuMind.Core.Core.Utils;

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
        AddSession(session);
        return session;
    }

    /// <summary>
    /// 复制聊天记录
    /// </summary>
    /// <param name="session"></param>
    public ChatSession CopySession(ChatSession session)
    {
        var sessionNew = GameUtils.Copy(session);
        AddSession(sessionNew);
        return sessionNew;
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
        DeleteSession(session);
        session.Name = newName;
        AddSession(session);
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
    public static void SaveChat(ChatSession chatSession, bool isAsNew = false)
    {
        if (isAsNew)
        {
            ChatManager.Instance.AddSession(chatSession);
            return;
        }

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
        // if (chatSessions.Count == 0)
        //     chatSessions.Add(new ChatSession(nameof(DefaultCharacter.Assistant),
        //         DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Assistant)));
        return chatSessions;
    }

    /// <summary>
    /// 添加聊天记录,如果添加的session的名称已经存在，则会自动修改名称变成一个新的
    /// 由于 session 本身引用不变,因此如果不是自己创建的 session 不要直接调用,不然容易导致旧的名字也被修改了(因为同属一个引用)
    /// </summary>
    /// <param name="session"></param>
    private void AddSession(ChatSession session)
    {
        session.Name = GetUniqueSessionName(session.Name);
        ChatSessions.Insert(0, session);
        SaveChat(session);
        OnChatSessionAdded?.Invoke(session);
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