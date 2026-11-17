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
/// 取消/失败收尾时，把「正在流的那一段」按<b>原始内容</b>补进历史。
///
/// <para>主路径（<c>TurnDriver</c>）与瞬态路径（<c>ChatSession.GenerateCompletionStreamingContent</c>）
/// 共用这一份：只要驱动侧把流里<b>可落盘的文本内容</b>（正文 <see cref="TextContent"/> 与
/// 结构化思考 <see cref="TextReasoningContent"/>）按到达顺序攒进缓冲，收尾时调
/// <see cref="TrimContents"/> 削掉「本轮已经落盘」的前缀，剩下的重建为一条助手消息落盘。
/// 思考不再像旧实现那样只补正文——本地模型的思考是嵌在正文文本里的内联标签，随
/// <see cref="TextContent"/> 走；远程模型的思考本来就是 <see cref="TextReasoningContent"/>，
/// 两者都原样保留，落盘形状与正常完成时的消息逐案一致（修复合入点见 ADR 0110）。</para>
///
/// <para>为什么是<b>内容级</b>去重而不是「按服务调用边界清缓冲」：落盘信号
/// （<see cref="MessageBoundaryContent"/>）由 <c>HarnessCharacterRunner</c> 在落盘那一刻异步塞进流，
/// 实测会晚于下一次调用的首条内容（见其注释）。缓冲若在边界清空，会把下一次调用刚流出的首段误清；
/// 不清、收尾时与历史比对削前缀，乱序只影响「能削多少」，方向仍是「宁可多写、不丢内容」。</para>
///
/// <para>不处理的内容：<see cref="EncryptedReasoningContent"/> 与 <see cref="ThinkingNotReturnedContent"/>
/// 是「思考未返回」的信号卡（没有可落盘的正文），<see cref="FunctionCallContent"/> / 
/// <see cref="FunctionResultContent"/> 是工具往返（未完成的结果由 <c>ToolCallCancellation</c> 另行补写），
/// 控制信令（<see cref="MessageBoundaryContent"/> 等）不进历史——这些都跳过。</para>
/// </summary>
internal static class InterruptedStreamSaver
{
    /// <summary>
    /// 取消收尾时往前扫多少条消息，用来认出「这段内容里哪些已经落盘」
    ///（与 TurnDriver 旧常量同源）。一轮最多几十次工具往返，这个深度足够
    /// </summary>
    private const int PersistedScanDepth = 32;

    /// <summary>
    /// 切点要拿多长的链去认。不看单条正文的长短：末尾那一句常常不到这个长度，
    /// 只拿「够长的那一条」去切，切点会往前移，把已经落盘的正文又写一遍；
    /// 太短也不行——「好。」这种在谁的话里都可能出现，拿它切会把真内容切掉
    /// </summary>
    private const int MinPersistedChainChars = 48;

    /// <summary>一条消息里可参与去重/落盘的文本类内容</summary>
    /// <param name="Content">原始内容</param>
    /// <param name="Start">在投影串里的起始偏移</param>
    /// <param name="Length">文本长度</param>
    internal readonly record struct TextUnit(AIContent Content, int Start, int Length);

    /// <summary>
    /// 这条内容是否属于「可落盘的文本类」：正文或结构化思考（文本非空）。
    /// 工具往返、用量、控制信令与「思考未返回」信号都不算——见类注释。
    /// </summary>
    /// <param name="content">流里的一段内容</param>
    /// <returns>可落盘为 true</returns>
    internal static bool IsPersistableText(AIContent content) =>
        content is TextContent { Text.Length: > 0 } or TextReasoningContent { Text.Length: > 0 };

    /// <summary>
    /// 一条历史消息「在去重链里长什么样」：正文与结构化思考<b>按 Contents 顺序</b>交错拼接。
    ///
    /// 不用 <c>ChatMessageDisplay.TextOf</c>（它只拼 <see cref="TextContent"/>，不含思考）——
    /// 历史里远程模型的思考是独立 <see cref="TextReasoningContent"/>，不拼进去的话，
    /// 缓冲里的思考段永远对不上已落盘链，每次取消都把思考再写一遍（多写且混乱）。
    /// 本地模型的内联 think 标签本来就嵌在 <see cref="TextContent"/> 文本里，无需特殊处理。
    /// </summary>
    /// <param name="message">一条历史消息</param>
    /// <returns>拼接文本；没有可拼内容为空字符串</returns>
    internal static string PersistedTextOf(ChatMessage message)
    {
        StringBuilder sb = new();
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    sb.Append(text.Text);
                    break;
                case TextReasoningContent { Text.Length: > 0 } reasoning:
                    sb.Append(reasoning.Text);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把缓冲里的可落盘内容投影成单一字符串，并留下每个单元的偏移。
    /// 用于前缀去重：拼接串顺序 == 缓冲到达顺序 == 落盘顺序，历史链才能对齐。
    /// </summary>
    /// <param name="pending">驱动侧攒下的流式缓冲（只含可落盘文本）</param>
    /// <param name="units">每个单元在投影串里的区间（按先后顺序）</param>
    /// <returns>投影串；缓冲区为空时为空字符串</returns>
    internal static string Project(IReadOnlyList<AIContent> pending, out List<TextUnit> units)
    {
        StringBuilder sb = new();
        units = new List<TextUnit>(pending.Count);
        foreach (AIContent content in pending)
        {
            string? text = (content as TextContent)?.Text ?? (content as TextReasoningContent)?.Text;
            if (string.IsNullOrEmpty(text)) continue;
            units.Add(new TextUnit(content, sb.Length, text.Length));
            sb.Append(text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 内容级去重：把缓冲里<b>本轮已经落盘</b>的前缀削掉，返回还没写进历史的那截。
    ///
    /// 全落盘时返回 null；剩余为空返回空列表；正常返回按到达顺序、保持原始形态
    /// （<see cref="TextContent"/> / <see cref="TextReasoningContent"/> 原样）的内容列表，
    /// 可直接用作一条助手消息的 Contents（边界单元按剩余长度重建）。
    /// </summary>
    /// <param name="history">会话历史<b>快照</b>（活引用会在取消路径上与落盘并发）</param>
    /// <param name="runStartedAt">本轮开始时刻；早于它的消息属于上一轮，不参与判定</param>
    /// <param name="pending">驱动侧攒下的流式缓冲快照</param>
    /// <returns>还没落盘的内容；全落盘为 null</returns>
    internal static List<AIContent>? TrimContents(IReadOnlyList<ChatMessage> history, DateTimeOffset? runStartedAt,
        IReadOnlyList<AIContent> pending)
    {
        string pendingText = Project(pending, out List<TextUnit> units);
        if (pendingText.Length == 0) return null;

        string? remaining = TrimPersistedPrefix(history, runStartedAt, pendingText);
        return RebuildFromSuffix(pendingText, units, remaining);
    }

    /// <summary>
    /// 削掉这段文本里<b>本轮已经落盘</b>的那些内容，只留还没写进历史的那一截。
    ///
    /// 判据只看本轮内的助手消息：驱动侧的缓冲按到达顺序攒文本，所以本轮那些已落盘的文本
    /// 必然是这段文本的一个前缀片段。两步，先精确后兜底：
    /// <list type="number">
    /// <item>从尾往前把本轮文本拼起来，拼出的东西正好是它的<b>后缀</b>——整段都落过盘了，一个字不写。
    /// 攒了整轮、或话刚说完就被停，走的都是这条。</item>
    /// <item>否则按「本轮最后一条够长的文本在缓冲里的最后一次出现」切掉前缀，剩下的就是还在流的那截。</item>
    /// </list>
    /// 两步都不命中就是全新内容，原样返回。
    /// </summary>
    /// <param name="history">会话历史<b>快照</b>（活引用会在取消路径上与落盘并发）</param>
    /// <param name="runStartedAt">本轮开始时刻；早于它的消息属于上一轮，不参与判定</param>
    /// <param name="text">驱动侧交上来的投影文本</param>
    /// <returns>还没落盘的那截；全都落过盘则为 null</returns>
    internal static string? TrimPersistedPrefix(IReadOnlyList<ChatMessage> history, DateTimeOffset? runStartedAt,
        string text)
    {
        // 本轮窗口里的助手文本，按落盘顺序。驱动侧的缓冲按到达顺序攒文本，所以它们拼起来
        // 必然是这段文本的一段连续前缀片段
        List<string> bodies = new();
        int oldest = Math.Max(0, history.Count - PersistedScanDepth);
        for (int i = history.Count - 1; i >= oldest; i--)
        {
            ChatMessage message = history[i];
            if (InThisRun(runStartedAt, message) is false) break;
            if (message.Role != ChatRole.Assistant) continue;

            string body = PersistedTextOf(message);
            if (body.Length > 0) bodies.Add(body);
        }

        if (bodies.Count == 0) return text;
        bodies.Reverse();
        StringBuilder chain = new();
        foreach (string body in bodies) chain.Append(body);
        string persistedText = chain.ToString();

        // 一、整段都已落盘：缓冲<b>恰好就是</b>这条链（攒了整轮，或话刚说完就被停）。
        // 这里判等而不是 EndsWith：链很短时（本轮只落过一句「好的。」）几乎任何新内容
        // 都能以它结尾，那就是把真正新写的字静默丢掉——方向反了
        if (text.Length == persistedText.Length && text.Equals(persistedText, StringComparison.Ordinal)) return null;

        // 二、常态：缓冲就是「这条链 + 新写的那截」。直接按链长切，不用搜索：
        // 搜索在「重复句」上会切错位置，而这条比较是确定的
        if (text.StartsWith(persistedText, StringComparison.Ordinal))
        {
            string tail = text[persistedText.Length..];
            return string.IsNullOrWhiteSpace(tail) ? null : tail;
        }

        // 三、链前面还夹着别的东西（上一发没落盘的残留，或落盘侧将前缀剥过）：
        // 从短链开始找，取<b>第一次</b>出现的位置。取最后一次的话，
        // 「先写新内容、末尾再逐字复述旧正文」会把中间那段真实新内容切掉（方向又反了）；
        // 重复命中时宁可多写
        chain.Clear();
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            chain.Insert(0, bodies[i]);
            if (chain.Length < MinPersistedChainChars) continue;

            string probe = chain.ToString();
            int position = text.IndexOf(probe, StringComparison.Ordinal);
            if (position < 0) continue;

            int rest = position + probe.Length;
            if (rest >= text.Length) return null;
            string remaining = text[rest..];
            return string.IsNullOrWhiteSpace(remaining) ? null : remaining;
        }

        return text;
    }

    /// <summary>
    /// 这条消息属不属于本轮。时间戳缺失（老存档里框架产出的副本）时不敢下结论：
    /// 返回 null 让调用方继续往前找（宁可多扫几条）
    /// </summary>
    /// <param name="runStartedAt">本轮开始时刻</param>
    /// <param name="message">待判定的消息</param>
    /// <returns>属于本轮为 true，属于更早的轮次为 false，判不了为 null</returns>
    private static bool? InThisRun(DateTimeOffset? runStartedAt, ChatMessage message)
    {
        if (runStartedAt is not { } started || message.CreatedAt is not { } at) return null;
        return at >= started;
    }

    /// <summary>
    /// 把去重后的剩余字符串还原成内容列表。
    ///
    /// <see cref="TrimPersistedPrefix"/> 只返回输入的后缀子串，所以剩余部分在投影串里的
    /// 起始位置是确定的；落在某个单元中间时，该单元按剩余长度重建为同类型的
    /// <see cref="TextContent"/> / <see cref="TextReasoningContent"/>，其余单元原样保留。
    /// </summary>
    /// <param name="pendingText">投影串（与 <paramref name="units"/> 对应）</param>
    /// <param name="units">投影单元</param>
    /// <param name="remaining">去重后的剩余串；全落盘为 null</param>
    /// <returns>未落盘内容；全落盘为 null</returns>
    private static List<AIContent>? RebuildFromSuffix(string pendingText, List<TextUnit> units, string? remaining)
    {
        if (remaining == null) return null;
        if (remaining.Length == 0) return [];

        // 防御：TrimPersistedPrefix 文档保证返回输入后缀。真走到这里说明逻辑被改过，
        // 宁可多写也不丢内容——把剩余按纯文本落盘（思考会退化成正文，可发现、可修）
        int offset = pendingText.Length - remaining.Length;
        if (offset < 0 || !pendingText.EndsWith(remaining, StringComparison.Ordinal))
        {
            return [new TextContent(remaining)];
        }

        List<AIContent> result = new(units.Count);
        for (int i = 0; i < units.Count; i++)
        {
            TextUnit unit = units[i];
            int unitEnd = unit.Start + unit.Length;
            if (unitEnd <= offset) continue; // 完全在已落盘前缀里
            if (unit.Start >= offset)
            {
                result.Add(unit.Content); // 完全未落盘：原样
                continue;
            }

            // 跨界单元：前缀削掉了头部，剩余部分重建
            int cut = offset - unit.Start;
            string tail = (unit.Content as TextContent)?.Text?[cut..]
                          ?? (unit.Content as TextReasoningContent)?.Text?[cut..] ?? string.Empty;
            if (tail.Length == 0) continue;
            result.Add(unit.Content is TextReasoningContent
                ? new TextReasoningContent(tail)
                : new TextContent(tail));
        }

        return result.Count == 0 ? null : result;
    }
}