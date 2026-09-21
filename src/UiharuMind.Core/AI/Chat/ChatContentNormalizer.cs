/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 助手消息内容的规整。修的是同一个成因：
///
/// 思考期服务端每个 SSE chunk 都同时给 <c>reasoning_content</c> 与一个空的 <c>content</c>，
/// 框架把它们原样收成 <c>[TextContent(""), TextReasoningContent(…)]</c>；
/// 而框架合并增量时只合并<b>连续同类型</b>的内容，被空正文一格一格切开后一条都并不掉。
/// 于是一条消息里堆出几十个空 <see cref="TextContent"/> 与几十段思考碎片：
///
/// <list type="number">
/// <item>空正文序列化后是一墙 <c>{"type":"text","text":""}</c>，白占提示词与前缀缓存；</item>
/// <item>思考被切碎后，按 tool_call 回填 <c>reasoning_content</c> 只能取到最后一个碎片。</item>
/// </list>
///
/// 流式侧在增量进入框架合并之前就丢掉空正文（思考段随之连续，框架自会并成一条）；
/// 已经落盘的老会话则在读取时补一道，历史文件不动，下次整体存盘自然写回干净的形态。
/// </summary>
public static class ChatContentNormalizer
{
    /// <summary>
    /// 是否为空正文/空思考增量（这类内容不携带任何信息，只会切断合并）
    /// </summary>
    /// <param name="content">待判定内容</param>
    /// <returns>是则为 true</returns>
    public static bool IsEmptyTextLike(AIContent content) => content switch
    {
        TextReasoningContent reasoning => reasoning.Text.Length == 0,
        TextContent text => text.Text.Length == 0,
        _ => false,
    };

    /// <summary>
    /// 规整一条消息的内容：丢掉空正文/空思考，并把相邻的思考碎片并回一段。
    /// 正文不做合并——框架的增量合并已经负责这件事，这里再并一次只会白丢
    /// <see cref="AIContent.AdditionalProperties"/>。
    /// </summary>
    /// <param name="message">待规整的消息（就地修改）</param>
    /// <returns>内容确有改动返回 true</returns>
    public static bool Normalize(ChatMessage message)
    {
        IList<AIContent> contents = message.Contents;
        if (contents.Count < 2) return false;
        if (!NeedsNormalize(contents)) return false;

        List<AIContent> rebuilt = new(contents.Count);
        StringBuilder? reasoning = null;
        foreach (AIContent content in contents)
        {
            if (IsEmptyTextLike(content)) continue;

            if (content is TextReasoningContent fragment)
            {
                (reasoning ??= new StringBuilder()).Append(fragment.Text);
                continue;
            }

            FlushReasoning(rebuilt, ref reasoning);
            rebuilt.Add(content);
        }

        FlushReasoning(rebuilt, ref reasoning);
        message.Contents = rebuilt;
        return true;
    }

    private static void FlushReasoning(List<AIContent> target, ref StringBuilder? reasoning)
    {
        if (reasoning == null) return;
        target.Add(new TextReasoningContent(reasoning.ToString()));
        reasoning = null;
    }

    // 绝大多数消息本就干净,先扫一遍避免无谓重建整个内容列表
    private static bool NeedsNormalize(IList<AIContent> contents)
    {
        bool previousIsReasoning = false;
        foreach (AIContent content in contents)
        {
            if (IsEmptyTextLike(content)) return true;

            bool isReasoning = content is TextReasoningContent;
            if (isReasoning && previousIsReasoning) return true;
            previousIsReasoning = isReasoning;
        }

        return false;
    }
}
