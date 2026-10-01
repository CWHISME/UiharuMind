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
            ConversationUsageViewData usage = new(() => contextLength, () => "m");

            usage.Refresh();
            contextLength = 2000;
            usage.Refresh();

            Assert.Equal(2000, usage.Ledger.ContextLength);
            Assert.Equal("m", usage.Context.ModelName);
        });
    }

    [Fact]
    public void RestoreFrom_TakesSessionTotals()
    {
        HeadlessUi.Run(() =>
        {
            ConversationUsageViewData usage = new(() => 0, () => "m");
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
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m");

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
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m");

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
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m");

            usage.RefreshCoalesced();
            usage.Dispose();

            await Task.Delay(PastDebounce);
            Assert.Equal(0, refreshes);
        });
    }

    [Fact]
    public void EstimateInput_Empty_ClearsAndRefreshesNow()
    {
        HeadlessUi.RunAsync(async () =>
        {
            int refreshes = 0;
            ConversationUsageViewData usage = new(() => ++refreshes, () => "m", _ => 5);

            usage.EstimateInput("abc");
            await WaitUntilAsync(() => usage.Ledger.InputEstimate == 5);
            int before = refreshes;

            usage.EstimateInput(string.Empty);

            Assert.Equal(0, usage.Ledger.InputEstimate);
            Assert.Equal(before + 1, refreshes);
        });
    }

    /// <summary>后台计数按完成先后回来，只有最新那次输入的数算数</summary>
    [Fact]
    public void EstimateInput_StaleCountIsIgnored()
    {
        HeadlessUi.RunAsync(async () =>
        {
            using ManualResetEventSlim releaseFirst = new();
            ConversationUsageViewData usage = new(() => 0, () => "m", text =>
            {
                if (text == "first") releaseFirst.Wait(TimeSpan.FromSeconds(5));
                return text.Length;
            });

            usage.EstimateInput("first");
            usage.EstimateInput("second!");
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
