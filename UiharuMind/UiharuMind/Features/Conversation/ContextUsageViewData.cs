/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 上下文占用的悬停面板数据：进度条位置、两条压缩水位刻度与档位配色。
///
/// 进度条整条 = 模型的上下文总长（用户能跟官方文档对上的那个数），
/// 而压缩水位是按**输入预算**（总长减去预留）算的，因此刻度位置必须换算过去，
/// 不能直接把 50%/80% 画在半腰和八分处。
/// </summary>
public partial class ContextUsageViewData : ObservableObject
{
    /// <summary>模型名</summary>
    [ObservableProperty] private string _modelName = string.Empty;

    /// <summary>上下文上限的显示文本</summary>
    [ObservableProperty] private string _limitText = string.Empty;

    /// <summary>「占用 / 上限」的显示文本</summary>
    [ObservableProperty] private string _usageText = string.Empty;

    /// <summary>本轮输入/输出的显示文本</summary>
    [ObservableProperty] private string _turnText = string.Empty;

    /// <summary>会话累计消耗的显示文本（与本轮同一形状，一眼看出是求和而非占用）</summary>
    [ObservableProperty] private string _sessionText = string.Empty;

    /// <summary>占用百分比（0~100），进度条的值</summary>
    [ObservableProperty] private double _usagePercent;

    /// <summary>三条水位的说明文本（折叠 → 交接 → 截断）</summary>
    [ObservableProperty] private string _thresholdText = string.Empty;

    /// <summary>前缀缓存命中的显示文本；服务端不报时为空</summary>
    [ObservableProperty] private string _cachedText = string.Empty;

    /// <summary>档位键，供样式选色：Normal / Evicting / Truncating</summary>
    [ObservableProperty] private string _stateKey = NormalState;

    /// <summary>是否有可显示的数据（没有就整个面板退回纯文本提示）</summary>
    [ObservableProperty] private bool _hasData;

    private const string NormalState = "Normal";
    private const string EvictingState = "Evicting";
    private const string TruncatingState = "Truncating";

    /// <summary>
    /// 按账本与当前模型刷新
    /// </summary>
    /// <param name="ledger">token 账本</param>
    /// <param name="modelName">当前模型名</param>
    public void Refresh(TurnUsageLedger ledger, string modelName)
    {
        int contextLength = ledger.ContextLength;
        ModelName = modelName;
        TurnText = ledger.TurnInput + ledger.TurnOutput > 0
            ? $"↑{TurnUsageLedger.Format(ledger.TurnInput)}  ↓{TurnUsageLedger.Format(ledger.TurnOutput)}"
            : string.Empty;
        // 与本轮同一形状(↑输入 ↓输出)。原先只给一个合计数,跟上一行的「上下文」对不上——
        // 那是占用(最近一次请求吃进去多少),这是累计消耗(每次请求的输入输出求和,
        // 一轮十几次工具往返会全部计入),两个轴不同,长得像就一定会被误读
        SessionText = ledger.SessionInput + ledger.SessionOutput > 0
            ? $"↑{TurnUsageLedger.Format(ledger.SessionInput)}  ↓{TurnUsageLedger.Format(ledger.SessionOutput)}"
            : string.Empty;

        if (contextLength <= 0)
        {
            HasData = false;
            LimitText = string.Empty;
            UsageText = string.Empty;
            ThresholdText = string.Empty;
            CachedText = string.Empty;
            UsagePercent = 0;
            StateKey = NormalState;
            return;
        }

        HasData = true;
        LimitText = TurnUsageLedger.Format(contextLength);
        // 还没收到过响应时占用是「未知」而不是「零」——显示 0/128k 会被读成「这个会话是空的」,
        // 而它可能只是刚切过来还没发过话
        UsageText = ledger.LastInput > 0
            ? $"{TurnUsageLedger.Format(ledger.LastInput)} / {LimitText}"
            : $"— / {LimitText}";
        UsagePercent = Percent(ledger.LastInput, contextLength);

        int budget = HistoryCompaction.InputBudgetFor(contextLength);
        double eviction = budget * HistoryCompaction.ToolEvictionThreshold;
        double handoff = budget * HistoryHandoff.Threshold;
        double truncation = budget * HistoryCompaction.TruncationThreshold;
        // 水位按输入预算(总长减预留)算,而进度条整条是总长——所以给绝对 token 数而不是百分比,
        // 两者的比例对不上。按量级排成一条递进的链,读起来就是"接下来会依次发生什么"
        ThresholdText = string.Format(LocalizationManager.Instance.GetString("ContextCompactionHint"),
            TurnUsageLedger.Format((long)eviction),
            TurnUsageLedger.Format((long)handoff),
            TurnUsageLedger.Format((long)truncation));

        // 配色按「接下来会发生什么」分档,不按水位数量分:
        // 折叠工具结果基本无损,不值得变色;真正该警示的是"要开始丢上下文了"
        StateKey = ledger.LastInput >= truncation
            ? TruncatingState
            : ledger.LastInput >= handoff
                ? EvictingState
                : NormalState;

        CachedText = ledger.LastCachedInput > 0
            ? $"{TurnUsageLedger.Format(ledger.LastCachedInput)} / {TurnUsageLedger.Format(ledger.LastInput)}"
            : string.Empty;
    }

    private static double Percent(double value, int total)
    {
        return total <= 0 ? 0 : Math.Clamp(value / total * 100, 0, 100);
    }
}
