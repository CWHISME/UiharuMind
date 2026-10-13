using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 工具卡的耗时侧：只显示不计时。
///
/// 计时的唯一权威是随内容流看的 Core 计时器（<c>ThinkingStatsRecorder</c>，调用发出到
/// 结果到达，同一把单调时钟），轮末盖进装着调用的那条历史消息；这里 live 时先按卡片
/// 创建时刻给一个 provisional，来源配好（或回放）后一律改读存档。
/// </summary>
public partial class ToolCallItem
{
    private readonly long _startTicks = Stopwatch.GetTimestamp(); //本次调用发出时刻（live provisional 用）
    private bool _hasPersistedDuration; //DurationText 已是存档值，收尾不再覆盖

    /// <summary>卡片头部显示的耗时（如「3.2s」）；没有数据时为空（不显示）</summary>
    [ObservableProperty] private string _durationText = string.Empty;

    /// <summary>
    /// 收尾 provisional：live 才调。回放不调——回放卡建完即收尾，起点是重建时刻，
    /// 算了也是假数，等 <see cref="FreezeReplayItems"/> 认领存档。
    /// </summary>
    internal void FinishProvisionalDuration()
    {
        if (_hasPersistedDuration) return;
        DurationText = FormatDuration(Stopwatch.GetElapsedTime(_startTicks));
    }

    /// <summary>
    /// 耗时显示格式：1 秒以上与思考段同一口径（秒/分/时），1 秒以内给毫秒。
    /// 快工具（读缓存、glob）真实耗时就是几十毫秒，按秒舍只会得到满屏的「0s」，
    /// 看着像没统计到——精度只给到毫秒，不给小数秒。
    /// </summary>
    /// <param name="span">耗时</param>
    /// <returns>显示文本</returns>
    internal static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds >= 1) return ThinkingItem.FormatDuration(span);
        long ms = (long)Math.Round(span.TotalMilliseconds);
        return ms <= 0 ? "<1ms" : $"{ms}ms";
    }

    /// <summary>认领存档耗时（回放定格与轮末覆盖都走这里）</summary>
    /// <param name="duration">存档耗时</param>
    internal void ApplyPersistedDuration(TimeSpan duration)
    {
        DurationText = FormatDuration(duration);
        _hasPersistedDuration = true;
    }

    /// <summary>
    /// 回放定格：命中存档读存档，未命中保持空白（老会话没有这个键，不拿假数充数）。
    /// 调用方在转录器收尾之后调——这时卡片的来源已指到装着调用的那条消息。
    /// </summary>
    /// <param name="items">同一条来源消息产出的工具卡片</param>
    /// <param name="message">来源历史消息</param>
    public static void FreezeReplayItems(IReadOnlyList<ToolCallItem> items, ChatMessage message)
    {
        if (items.Count == 0) return;
        if (!ChatMessageAnnotations.TryReadToolDuration(message, out TimeSpan duration)) return;
        foreach (ToolCallItem item in items) item.ApplyPersistedDuration(duration);
    }

    /// <summary>
    /// 自己那一轮跑完（或旁观的那一轮轮末）：来源已配好，把驱动方盖进历史的存档读回来，
    /// 盖掉 live 的 provisional。只读不写，不落盘。
    /// </summary>
    /// <param name="items">界面条目集合</param>
    /// <returns>改读存档的卡片数</returns>
    public static int AdoptPersistedDurations(IEnumerable<ConversationItemBase> items)
    {
        int adopted = 0;
        foreach (ToolCallItem card in items.OfType<ToolCallItem>())
        {
            if (card._hasPersistedDuration || card.SourceMessage == null) continue;
            if (!ChatMessageAnnotations.TryReadToolDuration(card.SourceMessage, out TimeSpan duration)) continue;
            card.ApplyPersistedDuration(duration);
            adopted++;
        }

        return adopted;
    }
}
