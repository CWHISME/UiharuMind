/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 忙标志作用域。这里真正要钉住的是<b>出错那条路</b>：
/// 界面卡死在忙态从来不是「点了没反应」，而是「点了、里面抛了、按钮再也回不来」，
/// 而这条路平时点一百遍都走不到，只有测试能替它走。
/// </summary>
public class AsyncCommandScopeTests
{
    /// <summary>正常跑完：忙标志先起后落，不惊动错误出口</summary>
    [Fact]
    public async Task NormalPath_RaisesThenClearsBusy()
    {
        List<bool> flips = new();
        bool errored = false;

        bool succeeded = await AsyncCommandScope.RunAsync(
            flips.Add,
            () => Task.CompletedTask,
            _ => errored = true);

        Assert.True(succeeded);
        Assert.Equal(new[] { true, false }, flips);
        Assert.False(errored);
    }

    /// <summary>同步抛：忙标志照样落下，异常原物交到错误出口</summary>
    [Fact]
    public async Task SynchronousThrow_ClearsBusyAndRoutesError()
    {
        List<bool> flips = new();
        Exception? routed = null;
        InvalidOperationException boom = new("boom");

        bool succeeded = await AsyncCommandScope.RunAsync(
            flips.Add,
            () => throw boom,
            e => routed = e);

        Assert.False(succeeded);
        Assert.Equal(new[] { true, false }, flips);
        Assert.Same(boom, routed);
    }

    /// <summary>await 之后才抛（真实的网络失败长这样）：忙标志仍复位</summary>
    [Fact]
    public async Task AsynchronousThrow_ClearsBusy()
    {
        bool busy = false;

        bool succeeded = await AsyncCommandScope.RunAsync(
            v => busy = v,
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            });

        Assert.False(succeeded);
        Assert.False(busy);
    }

    /// <summary>不给错误出口也不许把异常漏出去——调用方通常是即发即忘，漏出去就是进程级崩溃</summary>
    [Fact]
    public async Task Throw_WithoutErrorSink_IsSwallowed()
    {
        bool busy = true;

        bool succeeded = await AsyncCommandScope.RunAsync(
            v => busy = v,
            () => throw new InvalidOperationException("boom"));

        Assert.False(succeeded);
        Assert.False(busy);
    }

    /// <summary>取消与失败同一条路。这是刻意的契约，要分开说的调用方别用这个作用域</summary>
    [Fact]
    public async Task Cancellation_TakesTheSameRouteAsFailure()
    {
        bool busy = false;
        Exception? routed = null;

        bool succeeded = await AsyncCommandScope.RunAsync(
            v => busy = v,
            () => Task.FromCanceled(new CancellationToken(true)),
            e => routed = e);

        Assert.False(succeeded);
        Assert.False(busy);
        Assert.IsAssignableFrom<OperationCanceledException>(routed);
    }

    /// <summary>挡门放下时：操作不跑，忙标志一个字都不动（免得把别人的忙态给清了）</summary>
    [Fact]
    public async Task SkipIf_NeitherRunsNorTouchesBusy()
    {
        List<bool> flips = new();
        bool ran = false;

        bool succeeded = await AsyncCommandScope.RunAsync(
            flips.Add,
            () =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            skipIf: true);

        Assert.False(succeeded);
        Assert.False(ran);
        Assert.Empty(flips);
    }

    /// <summary>忙的时候再点一下：第二次被挡下，第一次照样跑完并放下忙标志</summary>
    [Fact]
    public async Task SecondCallWhileBusy_IsBlockedAndFirstStillFinishes()
    {
        bool busy = false;
        int runs = 0;
        TaskCompletionSource gate = new();

        Task<bool> first = AsyncCommandScope.RunAsync(
            v => busy = v,
            async () =>
            {
                runs++;
                await gate.Task;
            });

        Assert.True(busy); //忙标志是同步置起的，不用等调度

        bool second = await AsyncCommandScope.RunAsync(
            v => busy = v,
            () =>
            {
                runs++;
                return Task.CompletedTask;
            },
            skipIf: busy);

        Assert.False(second);
        Assert.Equal(1, runs);
        Assert.True(busy); //被挡下的那次不该顺手把忙态清掉

        gate.SetResult();
        Assert.True(await first);
        Assert.False(busy);
    }
}
