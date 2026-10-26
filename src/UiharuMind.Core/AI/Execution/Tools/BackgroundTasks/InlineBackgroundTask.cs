/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 进程内委托形态的后台任务：不跑 shell，任务体是一个返回结果正文的委托。
///
/// 存在的理由：有些「后台活」没有命令——等一个会话回完一轮、等一群跑完一波（SendMessage
/// 的等待任务）。它们同样要占后台任务的名额（面板可见、可叫停、退出收尾、结果送还 + 唤醒），
/// 但既没有进程树可杀、也没有日志文件。
///
/// ⚠️ 与 shell 任务的不同：<see cref="Kill"/> 只能取消执行体，**执行体必须响应传给它的
/// <see cref="CancellationToken"/>**。不响应时没有进程树可杀，只能靠 <see cref="KillGrace"/>
/// 到点强制收口——任务不会永远挂在「在跑」，但那一轮它会继续跑完。
/// </summary>
public sealed class InlineBackgroundTask : IBackgroundTask
{
    /// <summary>
    /// 叫停/超时之后给任务体的宽限期。过了还不收口就强制收口：
    /// shell 任务能杀进程树，这边没有那条退路，只能兜住「任务永远停在在跑」这个坑
    /// </summary>
    internal static TimeSpan KillGrace = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _cancel = new(); //任务体取消：只由 Kill 触发
    private readonly CancellationTokenSource _timeLimit = new(); //时间限制器与宽限：任务收口时停
    private readonly Func<CancellationToken, Task<string>> _body;
    private readonly TaskCompletionSource<BackgroundTaskOutcome> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _end = -1; //主动结束时先记下原因，取消回调据此区分

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string OwnerSessionId { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public string LogPath => string.Empty;

    /// <inheritdoc />
    public TimeSpan MaxRuntime { get; }

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <inheritdoc />
    public Task<BackgroundTaskOutcome> Completion => _completion.Task;

    /// <inheritdoc />
    public string? ResultText { get; private set; }

    private InlineBackgroundTask(string ownerSessionId, string description, TimeSpan maxRuntime,
        Func<CancellationToken, Task<string>> body)
    {
        Id = Guid.NewGuid().ToString("N")[..8];
        OwnerSessionId = ownerSessionId;
        Description = description;
        MaxRuntime = maxRuntime;
        _body = body;
    }

    /// <summary>
    /// 起一个进程内后台任务（fire-and-forget：任务体立刻开跑，结局经 <see cref="Completion"/> 交底）
    /// </summary>
    /// <param name="ownerSessionId">启动它的会话标识，结果送回这里</param>
    /// <param name="description">一句话说明，显示给用户</param>
    /// <param name="maxRuntime">时间上限；到点按 <see cref="EBackgroundTaskEnd.TimeLimit"/> 收口</param>
    /// <param name="body">任务体：返回结果正文，取消时抛 <see cref="OperationCanceledException"/></param>
    internal static InlineBackgroundTask Start(string ownerSessionId, string description, TimeSpan maxRuntime,
        Func<CancellationToken, Task<string>> body)
    {
        InlineBackgroundTask task = new(ownerSessionId, description, maxRuntime, body);
        _ = task.RunAsync();
        _ = task.EnforceTimeLimitAsync();
        return task;
    }

    /// <inheritdoc />
    public void Kill(EBackgroundTaskEnd reason)
    {
        if (Interlocked.CompareExchange(ref _end, (int)reason, -1) != -1) return;
        _cancel.Cancel();
        //任务体可能不响应令牌：宽限到点强制收口，不让它永远停在在跑
        _ = ForceSettleAsync(reason);
    }

    private async Task RunAsync()
    {
        string? result = null;
        EBackgroundTaskEnd end = EBackgroundTaskEnd.Completed;
        try
        {
            result = await _body(_cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 被 Kill 取消：原因已在 _end 里；自然完成但任务体主动取消（不该发生）按完成算
            end = _end < 0 ? EBackgroundTaskEnd.Completed : (EBackgroundTaskEnd)_end;
        }
        catch (Exception e)
        {
            // 任务体出错也照常落地结局：不能让它停在「在跑」上
            Log.Warning($"Inline background task failed: id={Id}: {e.Message}");
            result = $"等待过程出错：{e.Message}";
        }
        finally
        {
            // 停掉时间限制器与宽限那两支：任务已经收口，没必要再挂到 MaxRuntime / KillGrace
            _timeLimit.Cancel();
        }

        Settle(end, result);
    }

    /// <summary>收口：先到的结局与正文为准（强制收口与任务体自己收口可能撞上）</summary>
    private void Settle(EBackgroundTaskEnd end, string? result)
    {
        ResultText ??= result;
        if (!_completion.TrySetResult(new BackgroundTaskOutcome(this, end, null, DateTimeOffset.Now - StartedAt))) return;

        Log.Debug($"Inline background task ended: id={Id} session={OwnerSessionId} end={end} "
                  + $"duration={(DateTimeOffset.Now - StartedAt).TotalSeconds:0}s");
    }

    private async Task EnforceTimeLimitAsync()
    {
        try
        {
            await Task.Delay(MaxRuntime, _timeLimit.Token).ConfigureAwait(false);
            Kill(EBackgroundTaskEnd.TimeLimit);
        }
        catch (OperationCanceledException)
        {
            //先结束了
        }
    }

    private async Task ForceSettleAsync(EBackgroundTaskEnd reason)
    {
        try
        {
            await Task.Delay(KillGrace, _timeLimit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; //任务体自己收口了，宽限用不上
        }

        Log.Warning($"Inline background task did not stop within {KillGrace.TotalSeconds:0}s, settling anyway: "
                    + $"id={Id} session={OwnerSessionId} end={reason}");
        Settle(reason, null);
    }
}
