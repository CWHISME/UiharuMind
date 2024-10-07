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