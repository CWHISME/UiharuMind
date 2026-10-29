/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 用量显示的刷新时机。刷新次数按「上限委托被读了几次」数——每次刷新都现读一次上限，
/// 这正是它不缓存分母的那条口径。节流靠界面线程上的计时器，所以跑在无头调度线程上。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationUsageHeadlessTests
{
    private static readonly TimeSpan PastDebounce = TimeSpan.FromMilliseconds(600);

    [Fact]
    public void Refresh_ReadsContextLengthEveryTime()
    {
        HeadlessUi.Run(() =>
        {
            int contextLength = 1000;
            ConversationUsageViewData usage = new(() => contextLength, () => "m", () => 0);

            usage.Refresh();
            contextLength = 2000;
            usage.Refresh();

            Assert.Equal(2000, usage.Ledger.ContextLength);
            Assert.Equal("m", usage.Context.ModelName);
        });
    }

    /// <summary>生图张数单列一行：生过才出现，数取自会话而不是账本</summary>
    [Fact]
    public void Refresh_ShowsGeneratedImagesOnlyWhenAny()
    {
        HeadlessUi.Run(() =>
        {
            int images = 0;
            ConversationUsageViewData usage = new(() => 0, () => "m", () => images);

            usage.Refresh();
            Assert.Equal(string.Empty, usage.Context.GeneratedImagesText);

            images = 3;
            usage.Refresh();
            Assert.Equal("3", usage.Context.GeneratedImagesText);
        });
    }

    /// <summary>
    /// 环里那个百分比是<b>有效占用</b>（含服务端未计入的那截）除以会话上限——
    /// 与右侧栏那根进度条同一口径，换个形状不能换个数
    /// </summary>
    [Fact]
    public void Context_ReportsRingPercentAndTip()
    {
        HeadlessUi.Run(() =>
        {
            ConversationUsageViewData usage = new(() => 128_000, () => "m", () => 0);
            usage.Ledger.Add(new UsageDetails { InputTokenCount = 32_000, OutputTokenCount = 10 });

            usage.Refresh();

            Assert.True(usage.Context.HasRing);
            Assert.Equal("25%", usage.Context.PercentText);
            Assert.Contains("25%", usage.Context.RingTip);
        });
    }

    /// <summary>
    /// 还没跑过的新会话不画环：0% 的空圈不是「还剩多少空间」，是「还没开始」，
    /// 占着位置只会让人以为那里出了什么问题
    /// </summary>
    [Fact]
    public void Context_HidesRingWhenNothingIsUsed()
    {
        HeadlessUi.Run(() =>
        {
            ConversationUsageViewData usage = new(() => 128_000, () => "m", () => 0);

            usage.Refresh();

            Assert.False(usage.Context.HasRing);
            Assert.True(usage.Context.HasData); // 面板本身照样能算出 0/128k
        });
    }

    /// <summary>
    /// 上限未知（还没选模型）时环仍然在，只是空着：占用是从会话本体恢复出来的，
    /// 「这个会话现在有多大」跟选没选模型无关
    /// </summary>
    [Fact]
    public void Context_KeepsRingWithoutADenominator()
    {
        HeadlessUi.Run(() =>
        {
            ConversationUsageViewData usage = new(() => 0, () => "m", () => 0);
            usage.Ledger.Add(new UsageDetails { InputTokenCount = 12_000, OutputTokenCount = 10 });

            usage.Refresh();

            Assert.True(usage.Context.HasRing);
            Assert.False(usage.Context.HasData);
            Assert.Equal(string.Empty, usage.Context.PercentText);
        });
    }

    [Fact]
    public void RestoreFrom_TakesSessionTotals()
    {
        HeadlessUi.Run(() =>
        {
            ConversationUsageViewData usage = new(() => 0, () => "m", () => 0);
            ChatSession session = new("t", new CharacterData { CharacterId = "t" })
            {
                IsTransient = true,
                TotalInputTokens = 300,
                TotalOutputTokens = 40,
                LastInputTokens = 120,
                TotalReasoningTokens = 7,
            };

            usage.RestoreFrom(session);

            Assert.Equal(300, usage.Ledger.SessionInput);
            Assert.Equal(40, usage.Ledger.SessionOutput);
            Assert.Equal(120, usage.Ledger.LastInput);
            Assert.Equal(7, usage.Ledger.SessionReasoningTokens);
        });
    }

    [Fact]
    public void RefreshCoalesced_MergesBurstIntoOneRefresh()
    {
        HeadlessUi.RunAsync(async () =>
        {
            int refreshes = 0;
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m", () => 0);

            for (int i = 0; i < 5; i++) usage.RefreshCoalesced();
            Assert.Equal(0, refreshes);

            await Task.Delay(PastDebounce);
            Assert.Equal(1, refreshes);
        });
    }

    [Fact]
    public void RefreshCoalesced_Force_RefreshesNowAndDropsPending()
    {
        HeadlessUi.RunAsync(async () =>
        {
            int refreshes = 0;
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m", () => 0);

            usage.RefreshCoalesced();
            usage.RefreshCoalesced(force: true);
            Assert.Equal(1, refreshes);

            await Task.Delay(PastDebounce);
            Assert.Equal(1, refreshes);
        });
    }

    [Fact]
    public void Dispose_DropsPendingRefresh()
    {
        HeadlessUi.RunAsync(async () =>
        {
            int refreshes = 0;
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m", () => 0);

            usage.RefreshCoalesced();
            usage.Dispose();

            await Task.Delay(PastDebounce);
            Assert.Equal(0, refreshes);
        });
    }

    [Fact]
    public void CountDraft_Empty_ClearsAndRefreshesNow()
    {
        HeadlessUi.RunAsync(async () =>
        {
            int refreshes = 0;
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m", () => 0, _ => 5);

            usage.CountDraft("abc");
            await WaitUntilAsync(() => usage.Ledger.InputEstimate == 5);
            int before = refreshes;

            usage.CountDraft(string.Empty);

            Assert.Equal(0, usage.Ledger.InputEstimate);
            Assert.Equal(before + 1, refreshes);
        });
    }

    /// <summary>
    /// 清空输入框不触发分词。这条路径每敲一下都会走一次，而空文本没什么可数的——
    /// 从前那条「停手 200ms 后数一次」的防抖就是在这儿被每一下回车拖住的
    /// </summary>
    [Fact]
    public void ClearDraft_DoesNotCount()
    {
        HeadlessUi.RunAsync(async () =>
        {
            int counts = 0;
            ConversationUsageViewData usage = new(() => 0, () => "m", () => 0, text =>
            {
                Interlocked.Increment(ref counts);
                return text.Length;
            });

            usage.CountDraft("abc");
            await WaitUntilAsync(() => usage.Ledger.InputEstimate == 3);

            usage.ClearDraft();

            Assert.Equal(0, usage.Ledger.InputEstimate);
            Assert.Equal(1, counts);
        });
    }

    /// <summary>后台计数按完成先后回来，只有最新那次输入的数算数</summary>
    [Fact]
    public void CountDraft_StaleCountIsIgnored()
    {
        HeadlessUi.RunAsync(async () =>
        {
            using ManualResetEventSlim releaseFirst = new();
            ConversationUsageViewData usage = new(() => 0, () => "m", () => 0, text =>
            {
                if (text == "first") releaseFirst.Wait(TimeSpan.FromSeconds(5));
                return text.Length;
            });

            usage.CountDraft("first");
            usage.CountDraft("second!");
            await WaitUntilAsync(() => usage.Ledger.InputEstimate == "second!".Length);

            releaseFirst.Set();
            await Task.Delay(PastDebounce);

            Assert.Equal("second!".Length, usage.Ledger.InputEstimate);
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }
}
