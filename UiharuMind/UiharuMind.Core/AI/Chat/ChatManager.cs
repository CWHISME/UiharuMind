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
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Core.Chat;

public class ChatManager : Singleton<ChatManager>
{
    public readonly List<ChatSession> ChatSessions = LoadChatHistory();

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

        return chatSessions;
    }
}