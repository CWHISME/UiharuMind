/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// <b>当前已停用</b>——<see cref="SessionChatHistoryProvider"/> 不再调用它，那里写明了停用理由与接回条件。
/// 保留实现与测试，是因为「图片累积吃掉上下文」这个问题本身没有消失，只是眼下有更划算的解法。
///
/// 只把最近几条带图消息的图片真的发给模型，更早的降级成一句占位文本。
///
/// 图片一旦进了历史就会每一轮重传：第 20 轮对话还在重发第 1 轮那张截图，
/// 上传体积与 token 都是按轮次线性累加的。而框架的压缩把非文本内容按
/// <c>字节数 / 4</c> 估 token，老图片不清掉会让估算值一路虚高，压缩因此被过早触发。
///
/// 只改喂给模型的那一份：会话文件与界面渲染始终是全量原图，历史消息本身不被修改
/// （这里一律构造新消息，绝不原地改 <see cref="ChatMessage.Contents"/>）。
/// </summary>
internal static class HistoryImageWindow
{
    /// <summary>保留原图的最近带图消息条数</summary>
    internal const int KeepRecentImages = 2;

    private const string Placeholder = "[Earlier image omitted from context to save tokens.]";

    /// <summary>
    /// 降级历史中较早的图片
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <param name="keepRecent">保留原图的最近带图消息条数</param>
    /// <returns>可直接喂给模型的历史；无需改动时原样返回</returns>
    internal static IEnumerable<ChatMessage> DemoteOldImages(IReadOnlyList<ChatMessage> history, int keepRecent)
    {
        int firstKeptIndex = FindFirstKeptIndex(history, keepRecent);
        if (firstKeptIndex < 0) return history; //带图消息没超过上限,一条都不用改

        List<ChatMessage> result = new(history.Count);
        for (int i = 0; i < history.Count; i++)
        {
            ChatMessage message = history[i];
            result.Add(i < firstKeptIndex && HasImage(message) ? WithoutImages(message) : message);
        }

        return result;
    }

    /// <summary>
    /// 找出「从这条起的图片都保留」的下标
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <param name="keepRecent">保留原图的最近带图消息条数</param>
    /// <returns>下标；带图消息不足上限时为 -1（表示无需降级）</returns>
    private static int FindFirstKeptIndex(IReadOnlyList<ChatMessage> history, int keepRecent)
    {
        int kept = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (!HasImage(history[i])) continue;
            kept++;
            if (kept > keepRecent) return i + 1; //这一条已经超出保留窗,它之前(含它)的图片都要降级
        }

        return -1;
    }

    private static bool HasImage(ChatMessage message)
    {
        foreach (AIContent content in message.Contents)
        {
            if (content is DataContent data && data.HasTopLevelMediaType("image")) return true;
        }

        return false;
    }

    /// <summary>
    /// 复制一条消息，把其中的图片换成占位文本，其余内容原样保留
    /// </summary>
    /// <param name="message">原消息</param>
    /// <returns>新消息</returns>
    private static ChatMessage WithoutImages(ChatMessage message)
    {
        List<AIContent> contents = new(message.Contents.Count);
        bool noticed = false;
        foreach (AIContent content in message.Contents)
        {
            if (content is DataContent data && data.HasTopLevelMediaType("image"))
            {
                //一条消息里挂多张图时只留一句占位,不必逐张重复
                if (noticed) continue;
                noticed = true;
                contents.Add(new TextContent(Placeholder));
                continue;
            }

            contents.Add(content);
        }

        return new ChatMessage(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            AdditionalProperties = message.AdditionalProperties,
        };
    }
}
