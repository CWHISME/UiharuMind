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
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 上下文占用的悬停面板数据：进度条两段、三条压缩水位刻度与档位配色。
///
/// 进度条整条 = 模型的上下文总长（用户能跟官方文档对上的那个数），
/// 而压缩水位是按**输入预算**（总长减去预留）算的，因此刻度位置必须换算过去，
/// 不能直接把 50%/80% 画在半腰和八分处。
///
/// <b>进度条分两段</b>：实色那段是<b>报告占用</b>（服务端说的，原样显示、可与官方控制台对账），
/// 淡色那段是它<b>没计入</b>的部分。服务端未必报全量——实测 GLM4-Flash 的 <c>prompt_tokens</c>
/// 不含工具定义，少报近一半。把差额画出来而不是抹掉或顶替，是为了让「进度条显示 30%、
/// 交接文档却开始写了」这种误解<b>不可能发生</b>，而不是事后写一句 tooltip 去解释它。
/// 两者口径一致时第二段恒为 0，绝大多数模型上看不出区别。见 ADR 0009。
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

    /// <summary>报告占用的百分比（0~100），进度条<b>实色那段</b>的值</summary>
    [ObservableProperty] private double _usagePercent;

    /// <summary>有效占用的百分比（0~100），进度条<b>整条填充</b>的长度（含未计入那段）</summary>
    [ObservableProperty] private double _effectivePercent;

    /// <summary>「服务端报告」那一行；没收到过响应时为空</summary>
    [ObservableProperty] private string _reportedText = string.Empty;

    /// <summary>「未计入（估）」那一行；服务端口径与我们一致时为空，整行不出现</summary>
    [ObservableProperty] private string _unreportedText = string.Empty;

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
        long usage = ledger.EffectiveInput; //「还剩多少空间」一律用它,见类注释
        ModelName = modelName;
        TurnText = ledger.TurnInput + ledger.TurnOutput > 0
            ? $"↑{TurnUsageLedger.FormatExact(ledger.TurnInput)}  ↓{TurnUsageLedger.FormatExact(ledger.TurnOutput)}"
            : string.Empty;
        // 与本轮同一形状(↑输入 ↓输出)。原先只给一个合计数,跟上一行的「上下文」对不上——
        // 那是占用(最近一次请求吃进去多少),这是累计消耗(每次请求的输入输出求和,
        // 一轮十几次工具往返会全部计入),两个轴不同,长得像就一定会被误读
        SessionText = ledger.SessionInput + ledger.SessionOutput > 0
            ? $"↑{TurnUsageLedger.FormatExact(ledger.SessionInput)}  ↓{TurnUsageLedger.FormatExact(ledger.SessionOutput)}"
            : string.Empty;

        // 缓存命中与上限无关,有就报
        CachedText = ledger.LastCachedInput > 0
            ? $"{TurnUsageLedger.FormatExact(ledger.LastCachedInput)} / {TurnUsageLedger.FormatExact(ledger.LastInput)}"
            : string.Empty;

        if (contextLength <= 0)
        {
            // 上限不知道,但占用可能是从会话本体恢复出来的——「这个会话现在有多大」
            // 跟当前选没选模型无关,该显示的还是要显示,只是没有分母、也画不出进度条
            HasData = false;
            LimitText = string.Empty;
            UsageText = usage > 0 ? TurnUsageLedger.FormatExact(usage) : string.Empty;
            ThresholdText = string.Empty;
            UsagePercent = 0;
            EffectivePercent = 0;
            ReportedText = string.Empty;
            UnreportedText = string.Empty;
            StateKey = NormalState;
            return;
        }

        HasData = true;
        LimitText = TurnUsageLedger.Format(contextLength);
        // 还没收到过响应时占用是「未知」而不是「零」——显示 0/128k 会被读成「这个会话是空的」,
        // 而它可能只是刚切过来还没发过话
        UsageText = usage > 0
            ? $"{TurnUsageLedger.FormatExact(usage)} / {TurnUsageLedger.FormatExact(contextLength)}"
            : $"— / {TurnUsageLedger.FormatExact(contextLength)}";

        // 两段:实色画服务端报的,淡色补上它没计入的那截。整条填充到有效占用
        UsagePercent = Percent(ledger.LastInput, contextLength);
        EffectivePercent = Percent(usage, contextLength);
        ReportedText = ledger.LastInput > 0 ? TurnUsageLedger.FormatExact(ledger.LastInput) : string.Empty;
        // 口径一致时这一行整行不出现:绝大多数服务端报的就是全量,摆一个恒为 0 的数只是噪音
        UnreportedText = ledger.UnreportedInput > 0
            ? TurnUsageLedger.FormatExact(ledger.UnreportedInput)
            : string.Empty;

        int budget = HistoryCompaction.InputBudgetFor(contextLength);
        // 折叠与截断乘在**历史额度**上(输入预算减固定开销),而这根轴是全量,所以要把固定开销加回来;
        // 交接文档判的本就是有效占用,与这根轴同轴,乘输入预算即可。三者仍按量级递进
        int quota = HistoryCompaction.HistoryQuotaFor(contextLength, ledger.FixedOverhead);
        double eviction = ledger.FixedOverhead + quota * HistoryCompaction.ToolEvictionThreshold;
        double handoff = budget * HistoryHandoff.Threshold;
        double truncation = ledger.FixedOverhead + quota * HistoryCompaction.TruncationThreshold;
        // 水位按输入预算(总长减预留)算,而进度条整条是总长——所以给绝对 token 数而不是百分比,
        // 两者的比例对不上。按量级排成一条递进的链,读起来就是"接下来会依次发生什么"
        ThresholdText = string.Format(LocalizationManager.Instance.GetString("ContextCompactionHint"),
            TurnUsageLedger.FormatExact((long)eviction),
            TurnUsageLedger.FormatExact((long)handoff),
            TurnUsageLedger.FormatExact((long)truncation));

        // 配色按「接下来会发生什么」分档,不按水位数量分:
        // 折叠工具结果基本无损,不值得变色;真正该警示的是"要开始丢上下文了"。
        // 判据用有效占用——它就是三条水位实际比对的那个数,配色与压缩何时动手必须同源
        StateKey = usage >= truncation
            ? TruncatingState
            : usage >= handoff
                ? EvictingState
                : NormalState;
    }

    private static double Percent(double value, int total)
    {
        return total <= 0 ? 0 : Math.Clamp(value / total * 100, 0, 100);
    }
}
