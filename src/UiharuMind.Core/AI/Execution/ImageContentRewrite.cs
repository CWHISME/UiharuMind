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

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 「把一条消息里的几张图换成一句话」这件事只有一份实现。
///
/// 现在有两处会做（<see cref="History.InlineImageDegrading"/>：模型看不了图；
/// <see cref="History.ImageBudget"/>：请求超过图片总量预算），成因与文案不同、动作完全相同——
/// 复制消息、按顺序摘掉图、把占位<b>并进原来那块文字</b>（多个文本块会让部分 OpenAI 兼容网关 400）。
/// 分开写两份的结果已经出现过一次漂移：一份复制了 <see cref="TextContent.AdditionalProperties"/>、
/// 另一份丢了，换行也各写各的。
/// </summary>
internal static class ImageContentRewrite
{
    /// <summary>
    /// 这条内容是不是内联图片。判定只留这一处：投影、降级、预算三处必须认得同一批内容
    /// </summary>
    /// <param name="content">消息内容</param>
    /// <returns>是图片为 true</returns>
    public static bool IsImage(AIContent content) =>
        content is DataContent data && data.HasTopLevelMediaType("image");

    /// <summary>
    /// 复制一条消息，按顺序摘掉前 <paramref name="dropCount"/> 张图，并写入一句占位
    /// </summary>
    /// <param name="message">原消息</param>
    /// <param name="dropCount">从头摘几张；<see cref="int.MaxValue"/> 表示全摘</param>
    /// <param name="note">按实际摘掉的张数生成占位文案（多张合成一句，不必逐张重复）</param>
    /// <param name="separator">占位与原文之间的分隔；两处成因的历史字节不同，各用各的，免得白白断一次前缀</param>
    /// <returns>新消息；没有图可摘时返回同一实例</returns>
    public static ChatMessage DropImages(ChatMessage message, int dropCount, Func<int, string> note,
        string separator = "\n")
    {
        int images = 0;
        int textIndex = -1;
        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (IsImage(message.Contents[i])) images++;
            else if (textIndex < 0 && message.Contents[i] is TextContent) textIndex = i;
        }

        int dropped = Math.Min(Math.Max(0, dropCount), images);
        if (dropped == 0) return message;

        string text = note(dropped);
        List<AIContent> kept = new(message.Contents.Count);
        int removed = 0;
        for (int i = 0; i < message.Contents.Count; i++)
        {
            AIContent content = message.Contents[i];
            if (removed < dropped && IsImage(content))
            {
                removed++;
                continue;
            }

            kept.Add(i == textIndex ? AppendNote((TextContent)content, text, separator) : content);
        }

        // 整条只有图（用户只贴了张截图、一个字没打）：占位自己成一块文本
        if (textIndex < 0) kept.Add(new TextContent(text));

        return ChatMessageCopies.WithContents(message, kept);
    }

    /// <summary>
    /// 把占位并进现有文本块。<b>带上原块的 <c>AdditionalProperties</c></b>——那些是能序列化的字段，
    /// 漏掉就是静默丢数据（同 <see cref="ChatMessageCopies"/> 的口径）
    /// </summary>
    private static TextContent AppendNote(TextContent text, string note, string separator) =>
        new(string.IsNullOrEmpty(text.Text) ? note : $"{text.Text}{separator}{note}")
        {
            AdditionalProperties = text.AdditionalProperties,
        };
}
