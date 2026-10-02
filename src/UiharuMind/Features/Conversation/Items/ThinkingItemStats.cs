/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 思考条目的统计侧：速度、节流、回放冻结、写回历史。
///
/// 单独一个文件：<see cref="ConversationItems"/> 已 800+ 行，统计逻辑自成一切片，
/// 与截断/订阅/审批各不相干，不再往那里面堆。
/// </summary>
public partial class ThinkingItem
{
    private EStatsSource _statsSource = EStatsSource.Live;
    private TimeSpan _closedElapsed; //收尾那一刻的耗时,写回历史用它而不用 Now
    private long _closedChars; //收尾那一刻的全文长度
    private DateTime _lastStatsAt = DateTime.MinValue; //上次刷标题统计的时刻,节流用
    private static readonly TimeSpan MinLiveSpeedSpan = TimeSpan.FromSeconds(1); //流式中不满这么久不报速度
    private static readonly TimeSpan MinFinalSpeedSpan = TimeSpan.FromMilliseconds(100); //定格后短于这个不报速度

    // 标题统计现在显示的是谁的数
    private enum EStatsSource
    {
        Live, //还在流,本窗口计时
        Timed, //收尾了,快照可信,还没写回历史
        Stamped, //本窗口的快照已写回历史(显示值即存档值)
        CharsOnly, //回放时没有存档,只定格字数
        Persisted, //回放或旁观时读的存档
    }

    // 回放冻结:存档值已定,泵与收尾都不许重算
    private bool IsStatsFrozen => _statsSource is EStatsSource.CharsOnly or EStatsSource.Persisted;

    /// <summary>收尾快照的耗时（未收尾为零）</summary>
    public TimeSpan ClosedElapsed => _closedElapsed;

    /// <summary>收尾快照的全文长度（未收尾为零）</summary>
    public long ClosedChars => _closedChars;

    /// <summary>缓冲全文长度快照（任意线程可调）</summary>
    public int FullLength
    {
        get
        {
            lock (_bufferGate) return _buffer.Length;
        }
    }

    /// <summary>
    /// 拼标题统计。纯函数：同一输入永远同一输出，可单测。
    /// 还在流的时候 1 秒内不带速度：每拍重算，3 个字 / 0.1s 会跳出 30 字/秒这种瞬时值，下一拍又掉下去；
    /// 定格之后（收尾、存档）数不再变，不满一秒也照算。只是短到 0.1s 以下不算——
    /// 那多半是整段一个包到的，分母量的是包间隔，算出来的几万字/秒没有意义。
    /// 速度取整数，1s 节拍下小数只会制造无效跳动。
    /// </summary>
    /// <param name="elapsed">耗时</param>
    /// <param name="charCount">全文字符数</param>
    /// <param name="isFinal">数已定格（收尾、存档），不会再跳</param>
    /// <returns>标题栏文本</returns>
    public static string FormatStats(TimeSpan elapsed, long charCount, bool isFinal = false)
    {
        string count = charCount.ToString("N0");
        string speedSuffix = string.Empty;
        if (elapsed >= (isFinal ? MinFinalSpeedSpan : MinLiveSpeedSpan) && charCount > 0)
        {
            long speed = (long)Math.Round(charCount / elapsed.TotalSeconds);
            speedSuffix = string.Format(Loc.Text(LangKey.AgentThinkingSpeedFormat), speed.ToString("N0"));
        }

        return string.Format(Loc.Text(LangKey.AgentThinkingStatsFormat),
            FormatDuration(elapsed), count, speedSuffix);
    }

    /// <summary>
    /// 定格存档值（回放命中统计时调用）。此后泵的延迟冲刷与收尾都不再重算——
    /// 重建的 <see cref="_startedAt"/> 是打开会话那一刻，重算只会得到 0.1s 的假耗时。
    /// </summary>
    /// <param name="duration">存档耗时</param>
    /// <param name="chars">存档字数</param>
    public void ApplyPersistedStats(TimeSpan duration, long chars)
    {
        StatsText = FormatStats(duration, chars, isFinal: true);
        _closedElapsed = duration;
        _closedChars = chars;
        _statsSource = EStatsSource.Persisted; //存档里已有，不必再写回
    }

    /// <summary>
    /// 定格纯字数（回放未命中统计的老会话调用）。假耗时不如不显示。
    /// </summary>
    /// <param name="chars">全文长度</param>
    public void FreezeCharsOnly(long chars)
    {
        StatsText = string.Format(Loc.Text(LangKey.AgentThinkingCharsFormat), chars.ToString("N0"));
        _closedChars = chars;
        _statsSource = EStatsSource.CharsOnly;
    }

    /// <summary>
    /// 回放定格：命中存档读存档，未命中只留字数。调用方在转录器收尾之后、逐条配来源时调。
    ///
    /// 存档是整条消息的合并值，一条消息拆成多张卡（think/text 交替）时按各卡字数分回去——
    /// 每张都挂合并值的话，三段思考就显示成三倍耗时。
    /// </summary>
    /// <param name="items">同一条来源消息产出的思考条目，按显示顺序</param>
    /// <param name="message">来源历史消息</param>
    public static void FreezeReplayItems(IReadOnlyList<ThinkingItem> items, ChatMessage message)
    {
        if (items.Count == 0) return;
        if (!ChatMessageAnnotations.TryReadThinkingStats(message, out TimeSpan duration, out long chars))
        {
            foreach (ThinkingItem item in items) item.FreezeCharsOnly(item.FullLength);
            return;
        }

        if (items.Count == 1)
        {
            items[0].ApplyPersistedStats(duration, chars);
            return;
        }

        long[] lengths = items.Select(x => (long)x.FullLength).ToArray();
        long[] shares = ThinkingStatsRecorder.DistributeDurations((long)duration.TotalMilliseconds, lengths);
        for (int i = 0; i < items.Count; i++)
        {
            items[i].ApplyPersistedStats(TimeSpan.FromMilliseconds(shares[i]), lengths[i]);
        }
    }

    /// <summary>
    /// 旁观别人那一轮的窗口（子会话窗口）在轮末接上驱动方落盘的统计。来源须已配好（对账先配对再调这里）。
    ///
    /// 旁观窗口自己的计时不可信：中途打开时积压的内容是一口气补发的，计时从打开那一刻起算；
    /// 本轮已落盘的那几条回放时驱动方还没盖章，只能定格成纯字数。驱动方轮末盖好的值写在
    /// 同一批消息实例上，这里改读它。顺带把这些卡标成「已写回」——否则用户接着在这个窗口发话时，
    /// 自己那一轮的盖章会把旁观计时当成本轮的值写回历史，覆盖掉正确的那份。
    /// 自己那一轮的卡已写回过（显示值即存档值），不动。
    /// </summary>
    /// <param name="items">界面条目集合</param>
    /// <returns>改读存档的消息数</returns>
    public static int AdoptPersistedStats(IEnumerable<ConversationItemBase> items)
    {
        int adopted = 0;
        foreach (IGrouping<ChatMessage, ThinkingItem> group in items.OfType<ThinkingItem>()
                     .Where(x => x.SourceMessage != null)
                     .GroupBy(x => x.SourceMessage!))
        {
            if (!group.Any(x => x.NeedsPersistedStats)) continue;
            if (!ChatMessageAnnotations.TryReadThinkingStats(group.Key, out _, out _)) continue;
            FreezeReplayItems(group.ToList(), group.Key);
            adopted++;
        }

        return adopted;
    }

    // 旁观窗口自己计的时(收尾了、没写回过),或回放时还没盖章、只定格了字数
    private bool NeedsPersistedStats => _statsSource is EStatsSource.Timed or EStatsSource.CharsOnly;

    /// <summary>
    /// 把本轮新收尾的思考段统计写回它们对应的历史消息（就地写，调用方负责落盘）。
    ///
    /// 只给真正装着思考内容的消息盖章：取消打断的思考段没进历史，它回落到的来源是猜的，
    /// 盖上去等于把耗时记到别人账上。同一条消息有多段思考时合并（耗时与字数求和），
    /// 删除/分叉/压缩改写历史时不会错位。
    /// </summary>
    /// <param name="items">界面条目集合</param>
    /// <returns>被写上统计的消息数</returns>
    public static int StampLiveItems(IEnumerable<ConversationItemBase> items)
    {
        int stamped = 0;
        IEnumerable<IGrouping<ChatMessage, ThinkingItem>> groups = items.OfType<ThinkingItem>()
            .Where(x => x._statsSource == EStatsSource.Timed && x.SourceMessage != null)
            .GroupBy(x => x.SourceMessage!);
        foreach (IGrouping<ChatMessage, ThinkingItem> group in groups)
        {
            ChatMessage message = group.Key;
            if (!message.Contents.OfType<TextReasoningContent>().Any()) continue;
            long durationMs = 0;
            long chars = 0;
            foreach (ThinkingItem thinking in group)
            {
                durationMs += (long)thinking._closedElapsed.TotalMilliseconds;
                chars += thinking._closedChars;
                thinking._statsSource = EStatsSource.Stamped;
            }

            ChatMessageAnnotations.WriteThinkingStats(message, durationMs, chars);
            stamped++;
        }

        return stamped;
    }
}
