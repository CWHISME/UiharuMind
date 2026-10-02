/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 点名调用（<c>/技能名</c>）在消息上留下的注解的写入。
///
/// 读取方是气泡构造、回放、消息级操作与会话内搜索，读法在 Core 的
/// <see cref="ChatMessageAnnotations.NamedSkillInputOf"/>——搜索不在界面层，读写口径不能只活在这里。
/// </summary>
public static class NamedSkillAnnotations
{
    /// <summary>
    /// 给消息打上点名调用标记。用的是专门的键而非 _attribution——
    /// 后者会让消息不落盘,而点名调用的正文必须常驻历史才能持续生效。
    /// </summary>
    /// <param name="message">用户消息(正文已是注入内容)</param>
    /// <param name="invocation">调用产物</param>
    /// <param name="input">用户原样输入的那一行</param>
    public static void Mark(ChatMessage message, SkillInvocation invocation, string input)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[ChatMessageAnnotations.NamedSkill] = invocation.SkillName;
        message.AdditionalProperties[ChatMessageAnnotations.NamedSkillInput] = input;
    }
}
