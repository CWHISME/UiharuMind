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

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 点名调用（<c>/技能名</c>）在消息上留下的注解的读写。
///
/// 独立成一处而不是挂在补全面板上：写入方是输入区的补全那条线，
/// 而读取方是气泡构造、回放与消息级操作接线——后者与「输入框弹不弹候选」无关，
/// 让它们去依赖一个界面模块的静态方法只会把依赖方向绕反。
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

    /// <summary>
    /// 取点名调用消息里用户原样输入的那一行。落盘往返后值会变成 JsonElement,
    /// 因此一律经 ToString 读取,不能强转 string。
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>用户输入;不是点名调用消息则为 null</returns>
    public static string? InputOf(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(ChatMessageAnnotations.NamedSkillInput,
                out object? value) != true)
        {
            return null;
        }

        string? input = value?.ToString();
        return string.IsNullOrEmpty(input) ? null : input;
    }
}
