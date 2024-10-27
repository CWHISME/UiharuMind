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

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

#pragma warning disable SKEXP0001

namespace UiharuMind.Core.Core.Chat;

public struct ChatMessage
{
    /// <summary>
    /// 以 UTC 时间戳表示的消息发送时间
    /// </summary>
    public long Timestamp;

    /// <summary>
    /// 本条消息内容
    /// </summary>
    public ChatMessageContent Message;

    /// <summary>
    /// 消息角色名字
    /// </summary>
    public string CharactorName => Message.AuthorName ?? "系统";

    public ECharacter Character
    {
        get
        {
            if (Message.Role == AuthorRole.System) return ECharacter.System;
            if (Message.Role == AuthorRole.User) return ECharacter.User;
            if (Message.Role == AuthorRole.Assistant) return ECharacter.Assistant;
            if (Message.Role == AuthorRole.Tool) return ECharacter.Tool;
            return ECharacter.User;
        }
    }

    /// <summary>
    /// 将存储的 UTC 时间戳转换为本地时间字符串，格式为 "yyyy/MM/dd HH:mm:ss"
    /// </summary>
    public string LocalTimeString
    {
        get
        {
            DateTime utcTime = new DateTime(Timestamp, DateTimeKind.Utc);
            DateTime localTime = utcTime.ToLocalTime();
            return localTime.ToString("yyyy/MM/dd HH:mm:ss");
        }
    }
}