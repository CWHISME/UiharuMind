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
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 「缓存命中」那一行的口径。正文必须是<b>会话累计</b>，不是最近一次响应。
///
/// 最近一次那个数按构造就接近全命中：一轮 agent 里上下文单调增长，最后一发的前缀正好是上一发的
/// 全部；写交接那一发还会把这一对数整个覆写掉。拿它报命中率，面板上就会出现「99%」而历史其实没那么多。
/// </summary>
public class ContextUsageCacheRateTests
{
    private static ContextUsageViewData Refresh(TurnUsageLedger ledger)
    {
        ContextUsageViewData viewData = new();
        viewData.Refresh(ledger, "m", 0);
        return viewData;
    }

    [Fact]
    public void CachedText_ReportsTheSessionTotalNotTheLastCall()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 100_000 }); //冷启动，整轮未命中
        ledger.BeginTurn();
        ledger.Add(new UsageDetails { InputTokenCount = 120_000, CachedInputTokenCount = 118_000 });

        ContextUsageViewData viewData = Refresh(ledger);

        //会话累计把冷启动那一次未命中算进了分母；只报最后一次的话这里会是 98.3%
        Assert.Equal("53.6% · 118k / 220k", viewData.CachedText);
    }

    [Fact]
    public void CachedTip_LinesUpSessionTurnAndLastSideBySide()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 100_000 });
        ledger.BeginTurn();
        ledger.Add(new UsageDetails { InputTokenCount = 120_000, CachedInputTokenCount = 118_000 });

        ContextUsageViewData viewData = Refresh(ledger);

        //本轮与最近一次都摆进悬停：它们才是「缓存此刻有没有在生效」的诊断值，正文那个不是
        Assert.Contains("53.6% · 118k / 220k", viewData.CachedTip);
        Assert.Contains("98.3% · 118k / 120k", viewData.CachedTip);
    }

    [Fact]
    public void CachedText_StaysEmptyWhenTheProviderNeverReportsHits()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 100_000 });

        ContextUsageViewData viewData = Refresh(ledger);

        Assert.Equal(string.Empty, viewData.CachedText);
        Assert.Equal(string.Empty, viewData.CachedTip);
    }

    [Fact]
    public void CachedTip_DashesTheScopesThatHaveNoDataYet()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 100_000, CachedInputTokenCount = 100_000 });
        //新一轮还没开始：本轮那一档没有数。悬停里留个占位符,少一行会被读成「那一档不存在」
        ledger.BeginTurn();

        ContextUsageViewData viewData = Refresh(ledger);

        Assert.Contains("—", viewData.CachedTip);
        Assert.Contains("100% · 100k / 100k", viewData.CachedTip);
    }

    /// <summary>本轮消耗不再单占一行，收进累计消耗的悬停</summary>
    [Fact]
    public void SessionTip_CarriesThisTurn()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 20 });
        ledger.BeginTurn();
        ledger.Add(new UsageDetails { InputTokenCount = 2000, OutputTokenCount = 40 });

        ContextUsageViewData viewData = Refresh(ledger);

        Assert.Equal("↑3k  ↓60", viewData.SessionText);
        Assert.Contains("↑2k  ↓40", viewData.SessionTip);
    }

    /// <summary>请求次数报会话累计，且跨轮累计（每一轮的响应都算一次请求）</summary>
    [Fact]
    public void RequestCountText_ReportsTheSessionTotalAcrossTurns()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 1000 });
        ledger.Add(new UsageDetails { InputTokenCount = 2000 });
        ledger.BeginTurn();
        ledger.Add(new UsageDetails { InputTokenCount = 3000 });
        ledger.Add(new UsageDetails { InputTokenCount = 4000 });

        ContextUsageViewData viewData = Refresh(ledger);

        Assert.Equal("4", viewData.RequestCountText);
    }

    [Fact]
    public void RequestCountText_StaysEmptyWhenTheModelWasNeverCalled()
    {
        ContextUsageViewData viewData = Refresh(new TurnUsageLedger());

        Assert.Equal(string.Empty, viewData.RequestCountText);
    }
}