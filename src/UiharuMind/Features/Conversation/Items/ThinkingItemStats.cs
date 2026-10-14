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
    private TimeSpan _closedElapsed; //收尾那一刻的耗时,回放对账用它而不用 Now
    private long _closedChars; //收尾那一刻的全文长度
    private DateTime _lastStatsAt = DateTime.MinValue; //上次刷标题统计的时刻,节流用

    // 标题统计现在显示的是谁的数
    private enum EStatsSource
    {
        Live, //还在流,本窗口计时（provisional，轮末被存档覆盖）
        Timed, //收尾了,快照是本窗口的（provisional，等存档覆盖）
        CharsOnly, //回放时没有存档,只定格字数
        Persisted, //存档（回放读的，或本轮轮末认领的），唯一可信值
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
    /// 有字数、有时间就报速度，不设时长门槛——限速门只会把 burst 到达（整段一次到、
    /// 分母极小）误判成极速，却修不好它；真正的分母来自 Core 计时器（到达时刻的单调时钟）。
    /// live（未定格）1 秒以上向下取整到整秒：节拍回调受 UI 繁忙度影响总晚到几十~上百毫秒，
    /// 按实际流逝四舍五入会稳定地多出 0.1（满屏 1.1s、2.1s），与工具卡跑表同一口径；
    /// 时长与速度用同一个取整后的值算，读者能对上。定格（收尾、存档）保持精确。
    /// 速度取整数，小数在标题栏里只会制造无效跳动。
    /// </summary>
    /// <param name="elapsed">耗时</param>
    /// <param name="charCount">全文字符数</param>
    /// <param name="isFinal">数已定格（收尾、存档），不会再跳</param>
    /// <returns>标题栏文本</returns>
    public static string FormatStats(TimeSpan elapsed, long charCount, bool isFinal = false)
    {
        if (!isFinal && elapsed.TotalSeconds >= 1)
            elapsed = TimeSpan.FromSeconds(Math.Floor(elapsed.TotalSeconds));
        string count = charCount.ToString("N0");
        string speedSuffix = string.Empty;
        if (elapsed > TimeSpan.Zero && charCount > 0)
        {
            long speed = (long)Math.Round(charCount / elapsed.TotalSeconds);
            speedSuffix = string.Format(Loc.Text(LangKey.AgentThinkingSpeedFormat), speed.ToString("N0"));
        }

        return string.Format(Loc.Text(LangKey.AgentThinkingStatsFormat),
            FormatDuration(elapsed), count, speedSuffix);
    }

    /// <summary>
    /// 定格存档值（回放命中统计时调用）。此后泵的延迟冲刷与收尾都不再重算——
    /// 重建卡片的计时起点是打开会话那一刻，重算只会得到 0.1s 的假耗时。
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
    /// 旁观别人那一轮的窗口（子会话窗口）在轮末认领驱动方落盘的统计；自己那一轮的卡
    /// 在 <c>Persisted</c> 通知里走同一条——来源须已配好（对账先配对再调这里）。
    ///
    /// 只读不写：计时的唯一权威是随内容流看的 Core 计时器（轮末已盖进历史），
    /// 这里只把存档读回来显示。本窗口自己的计时只是 provisional（中途打开时积压的内容
    /// 是一口气补发的，计时从打开那一刻起算；live 卡的 wall-clock 含 UI 分发延迟）——
    /// 拿它写回历史就是把假数盖到真数上，这正是从前速度与耗时对不上的根源。
    /// 本轮已落盘的那几条回放时驱动方还没盖章，只能定格成纯字数。驱动方轮末盖好的值写在
    /// 同一批消息实例上，这里改读它。
    /// 已认领过存档的卡（显示值即存档值），不动。
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

    // 本窗口自己计的 provisional（收尾了、还没认领存档），或回放时没命中存档只定了字数
    private bool NeedsPersistedStats => _statsSource is EStatsSource.Timed or EStatsSource.CharsOnly;
}
