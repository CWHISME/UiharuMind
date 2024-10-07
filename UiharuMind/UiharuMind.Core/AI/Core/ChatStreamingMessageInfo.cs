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

namespace UiharuMind.Core.AI.Core;

public struct ChatStreamingMessageInfo
{
    public string Message = String.Empty;
    public int TokenCount = 0;

    public ChatStreamingMessageInfo()
    {
    }

    public ChatStreamingMessageInfo(string message)
    {
        Message = message;
    }
}