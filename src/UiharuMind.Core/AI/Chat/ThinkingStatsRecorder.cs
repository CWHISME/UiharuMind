/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 思考段计时器：随 <see cref="UiharuMind.Core.AI.Execution.TurnDriver"/> 看同一条内容流，
/// 轮末给本轮历史里缺统计的思考消息盖章（耗时 + 字数，速度由两数现算）。
///
/// 边界规则不自立：经 <see cref="ThinkingBoundary"/> 分发，与界面转录器同一份——
/// 终结段时连解析器扣留的半截标签一起定夺，否则两边的 parser 会跨段错位。
/// 界面侧的盖章保留——显示计时更贴合所见，轮末会覆盖这里的值；
/// 这里是无界面轮次（子代理、定时任务、外驱观察）的兜底，兼界面配对失败时的回填。
/// 只写缺统计的消息，已有值的不碰。
/// </summary>
public sealed class ThinkingStatsRecorder
{
    private readonly ThinkTagStreamParser _thinkParser = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(long Ms, long Chars)> _segments = new(); //本轮已收尾的思考段，按到达顺序
    private TimeSpan _segmentStart; //当前思考段的开始时刻（相对 _clock）
    private bool _segmentOpen; //正处在一段思考里
    private long _segmentChars; //当前思考段的字数
    private TimeSpan _totalThinking; //本轮思考段耗时之和
    private long _totalThinkingChars; //本轮思考字数之和

    /// <summary>本轮已累计的思考耗时</summary>
    public TimeSpan TotalThinking => _totalThinking + (_segmentOpen ? _clock.Elapsed - _segmentStart : TimeSpan.Zero);

    /// <summary>本轮已累计的思考字数</summary>
    public long TotalThinkingChars => _totalThinkingChars;

    /// <summary>
    /// 喂入一段内容。由 <see cref="UiharuMind.Core.AI.Execution.TurnDriver"/> 在转发给渲染落点时顺带调用。
    /// </summary>
    /// <param name="content">本轮内容流里的一段</param>
    public void NoteContent(AIContent content)
    {
        ThinkingBoundary.Dispatch(content, _thinkParser, _ => CloseSegment(), AddThinking, NoteSegmentClosed);
    }

    /// <summary>
    /// 收尾当前流段（轮次内每段结束、服务调用边界时调用，与转录器的 CloseSegment 对齐）。
    /// </summary>
    public void NoteSegmentClosed()
    {
        ThinkingBoundary.Complete(_thinkParser, _ => CloseSegment(), AddThinking, CloseSegment);
    }

    /// <summary>
    /// 给本轮新增且缺统计的思考消息盖章。先收尾未闭合的段（失败路径可能没走 CloseSegment），
    /// 再按到达顺序把各段的真实耗时归到各自的消息上；段与消息对不上（取消截掉的半段没进历史、
    /// 有消息已带统计被跳过）时才退回按字数比例分摊总耗时。
    ///
    /// 不能一上来就按比例摊：一轮多次工具往返时各次思考的快慢差得很远，
    /// 摊下来是「想了半分钟的那条显示 3 秒、几秒的那条显示半分钟」。
    /// </summary>
    /// <param name="history">会话历史（就地写）</param>
    /// <param name="fromIndex">本轮新增段的起始下标，该位置之前的消息不动</param>
    /// <returns>被写上统计的消息数</returns>
    public int Stamp(IList<ChatMessage> history, int fromIndex)
    {
        NoteSegmentClosed();
        if (_totalThinkingChars <= 0) return 0;

        int start = Math.Clamp(fromIndex, 0, history.Count);
        List<ChatMessage> targets = new();
        List<long> weights = new();
        for (int i = start; i < history.Count; i++)
        {
            ChatMessage message = history[i];
            if (message.Role != ChatRole.Assistant) continue; //思考只会是助手的；用户原文里的 <think> 字样不能算
            if (ChatMessageAnnotations.TryReadThinkingStats(message, out _, out _)) continue;
            long chars = ThinkingCharsOf(message);
            if (chars <= 0) continue;
            targets.Add(message);
            weights.Add(chars);
        }

        if (targets.Count == 0) return 0;
        long[] shares = AttributeSegments(_segments, weights.ToArray())
                        ?? DistributeDurations((long)TotalThinking.TotalMilliseconds, weights.ToArray());
        for (int i = 0; i < targets.Count; i++)
        {
            ChatMessageAnnotations.WriteThinkingStats(targets[i], shares[i], weights[i]);
        }

        return targets.Count;
    }

    /// <summary>
    /// 按到达顺序把思考段归到消息上：每条消息依次吃掉字数正好凑满它的那几段（同一条消息可以有多段）。
    /// 纯函数。段与消息的字数对不上就返回 null，由调用方退回比例分摊。
    /// </summary>
    /// <param name="segments">本轮各思考段的耗时与字数，按到达顺序</param>
    /// <param name="weights">各消息的思考字数，按历史顺序</param>
    /// <returns>各消息的耗时毫秒数；对不上为 null</returns>
    public static long[]? AttributeSegments(IReadOnlyList<(long Ms, long Chars)> segments, long[] weights)
    {
        long[] shares = new long[weights.Length];
        int next = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            long chars = 0;
            while (chars < weights[i] && next < segments.Count)
            {
                chars += segments[next].Chars;
                shares[i] += segments[next].Ms;
                next++;
            }

            if (chars != weights[i]) return null;
        }

        return next == segments.Count ? shares : null;
    }

    /// <summary>
    /// 把总毫秒数按权重分摊（最大余数法，分摊之和恒等于总量）。
    /// 纯函数，单测钉住它，改了分摊规则不会静默改变落盘值。
    /// </summary>
    /// <param name="totalMs">待分摊的总毫秒数</param>
    /// <param name="weights">各消息的思考字数</param>
    /// <returns>各消息分到的毫秒数，与 <paramref name="weights"/> 等长</returns>
    public static long[] DistributeDurations(long totalMs, long[] weights)
    {
        long[] shares = new long[weights.Length];
        long totalWeight = 0;
        foreach (long weight in weights) totalWeight += weight;
        if (weights.Length == 0 || totalWeight <= 0 || totalMs <= 0) return shares;

        long assigned = 0;
        long[] remainders = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            long scaled = totalMs * weights[i];
            shares[i] = scaled / totalWeight;
            remainders[i] = scaled % totalWeight;
            assigned += shares[i];
        }

        long left = totalMs - assigned;
        foreach (int index in remainders
                     .Select((remainder, index) => (remainder, index))
                     .OrderByDescending(x => x.remainder)
                     .ThenBy(x => x.index)
                     .Select(x => x.index))
        {
            if (left <= 0) break;
            shares[index]++;
            left--;
        }

        return shares;
    }

    /// <summary>
    /// 一条消息里的思考字数：结构化思考段求和，正文里的 &lt;think&gt; 段经解析器提取后求和。
    /// </summary>
    internal static long ThinkingCharsOf(ChatMessage message)
    {
        long chars = 0;
        ThinkTagStreamParser? parser = null;
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning:
                    chars += reasoning.Text?.Length ?? 0;
                    break;
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    parser ??= new ThinkTagStreamParser();
                    parser.Feed(text.Text, _ => { }, segment => chars += segment.Length);
                    break;
            }
        }

        if (parser != null)
        {
            long held = chars;
            parser.Complete(_ => { }, segment => held += segment.Length);
            chars = held;
        }

        return chars;
    }

    private void AddThinking(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        if (!_segmentOpen)
        {
            _segmentOpen = true;
            _segmentStart = _clock.Elapsed;
            _segmentChars = 0;
        }

        _segmentChars += delta.Length;
        _totalThinkingChars += delta.Length;
    }

    private void CloseSegment()
    {
        if (!_segmentOpen) return;
        _segmentOpen = false;
        TimeSpan elapsed = _clock.Elapsed - _segmentStart;
        _totalThinking += elapsed;
        _segments.Add(((long)elapsed.TotalMilliseconds, _segmentChars));
    }
}
