/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using System.Text.Json;
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
///
/// 另一个成因是<b>工具结果的落盘往返</b>：字符串结果（Read/Grep/Glob/Write…）序列化存盘后再
/// 读回，<see cref="FunctionResultContent.Result"/> 变成 <c>JsonElement{String}</c>，发给模型与界面时
/// 会被整体再序列化（换行变 <c>\n</c> 字面量）或 ToString 成带引号文本——这里一并还原成字符串。
/// </summary>
public static class ChatContentNormalizer
{
    /// <summary>
    /// 是否为空正文/空思考增量（这类内容不携带任何信息，只会切断合并）
    /// </summary>
    /// <param name="content">待判定内容</param>
    /// <returns>是则为 true</returns>
    /// <remarks>
    /// <b>带密文的空思考不算空</b>：加密思考的正文永远不下发，思考内容只有
    /// <see cref="TextReasoningContent.ProtectedData"/> 一项（上游 M.E.AI 明说这种形态合法）。
    /// 把它当空思考丢掉，票据就跟着没了——下一发回填不了密文，推理链断，
    /// 而且丢的那条消息与重启前的字节不一致，前缀缓存当场分叉。
    /// </remarks>
    public static bool IsEmptyTextLike(AIContent content) => content switch
    {
        TextReasoningContent reasoning => reasoning.Text.Length == 0 && string.IsNullOrEmpty(reasoning.ProtectedData),
        TextContent text => text.Text.Length == 0,
        _ => false,
    };

    /// <summary>
    /// 规整一条消息的内容：把工具结果的 <c>JsonElement{String}</c> 还原成字符串，
    /// 丢掉空正文/空思考，并把相邻的思考碎片并回一段。
    /// 正文不做合并——框架的增量合并已经负责这件事，这里再并一次只会白丢
    /// <see cref="AIContent.AdditionalProperties"/>。
    /// </summary>
    /// <param name="message">待规整的消息（就地修改）</param>
    /// <returns>内容确有改动返回 true</returns>
    public static bool Normalize(ChatMessage message)
    {
        IList<AIContent> contents = message.Contents;
        bool changed = RestoreStringResults(message);
        if (contents.Count < 2) return changed;
        if (!NeedsNormalize(contents)) return changed;

        List<AIContent> rebuilt = new(contents.Count);
        StringBuilder? reasoning = null;
        foreach (AIContent content in contents)
        {
            if (IsEmptyTextLike(content)) continue;

            if (content is TextReasoningContent fragment)
            {
                // 带密文的思考**不能并进文本片段**：并进去就等于丢掉密文——
                // 合并出来的是一条全新的 TextReasoningContent，ProtectedData 不会被带过去。
                // 票据一丢，下一发回填不了密文，推理链断，且那条消息与重启前字节不一致，
                // 前缀缓存当场分叉。所以它原样保留，不参与片段合并。
                if (!string.IsNullOrEmpty(fragment.ProtectedData))
                {
                    FlushReasoning(rebuilt, ref reasoning);
                    rebuilt.Add(fragment);
                    continue;
                }

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

    /// <summary>落盘再读回的工具字符串结果从 <c>JsonElement{String}</c> 还原成 string（与 <see cref="ImagePreviewProjection"/> 同口径）</summary>
    private static bool RestoreStringResults(ChatMessage message)
    {
        bool changed = false;
        foreach (AIContent content in message.Contents)
        {
            if (content is not FunctionResultContent result) continue;
            if (result.Result is JsonElement { ValueKind: JsonValueKind.String } element)
            {
                result.Result = element.GetString();
                changed = true;
            }
        }

        return changed;
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
