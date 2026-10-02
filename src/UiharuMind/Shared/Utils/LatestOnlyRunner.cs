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
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 「停手一会儿再跑、只认最新一次」：搜索框那类每敲一个字都想重跑、但只有最后一次的结果有用的活。
///
/// 被取代 ⇔ 令牌已取消，所以调用方不用再另记一个版本号去比——每次 await 回来问一句令牌就够了。
/// 等待用 <c>Task.Delay</c>、到点经 <c>post</c> 回 UI 线程跑：测试传同步执行就不用拉起界面调度器，
/// 也能 await <see cref="Pending"/> 等它跑完，不必真的睡过防抖
/// </summary>
public sealed class LatestOnlyRunner : IDisposable
{
    private readonly Action<Action> _post;
    private CancellationTokenSource? _current;

    /// <summary>构造</summary>
    /// <param name="post">回 UI 线程的方式</param>
    public LatestOnlyRunner(Action<Action> post)
    {
        _post = post;
    }

    /// <summary>最近一次排的那次：跑完、被取代或被取消时完成，不抛异常</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 排一次：等 <paramref name="delay"/> 之后在 UI 线程跑（零就直接投递）；期间再排或取消就作废
    /// </summary>
    /// <param name="delay">防抖</param>
    /// <param name="run">要跑的；令牌取消即被取代，await 回来先看它</param>
    /// <returns>同 <see cref="Pending"/></returns>
    public Task Schedule(TimeSpan delay, Func<CancellationToken, Task> run)
    {
        CancellationToken token = Restart();
        return Pending = DelayThenRunAsync(delay, run, token);
    }

    /// <summary>立即跑一次（作废之前排着与在跑的），在调用线程上开始</summary>
    /// <param name="run">要跑的</param>
    /// <returns>同 <see cref="Pending"/></returns>
    public Task RunNow(Func<CancellationToken, Task> run)
    {
        CancellationToken token = Restart();
        return Pending = GuardAsync(run, token);
    }

    /// <summary>作废排着与在跑的那次</summary>
    public void Cancel() => _current?.Cancel();

    /// <inheritdoc />
    public void Dispose() => Cancel();

    private CancellationToken Restart()
    {
        _current?.Cancel();
        _current = new CancellationTokenSource();
        return _current.Token;
    }

    private async Task DelayThenRunAsync(TimeSpan delay, Func<CancellationToken, Task> run, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        TaskCompletionSource done = new();
        _post(() => _ = CompleteAfter(GuardAsync(run, token), done));
        await done.Task;
    }

    private static async Task CompleteAfter(Task run, TaskCompletionSource done)
    {
        await run;
        done.SetResult();
    }

    // 异常不往外漏:这些活都是即发即忘的,漏出去就是一个没人观察的任务异常,界面停在半截状态
    private static async Task GuardAsync(Func<CancellationToken, Task> run, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        try
        {
            await run(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Error($"Latest-only run failed: {e}");
        }
    }
}
