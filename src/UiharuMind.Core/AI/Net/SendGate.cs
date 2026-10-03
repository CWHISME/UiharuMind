/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 共用一份限额的请求按 <see cref="SendPacer"/> 的间隔排队发（ADR 0058）。
/// 按「端点 + 密钥 + 模型名」分组：群里几名成员挂同一把密钥并行时，原先各自退避、互不知情，
/// 一起撞、一起退、再一起撞，单次调用能连撞二十来次。
/// 闸没合上（间隔为零、没人排队）时直接放行，不付任何代价；被 429 打回的重试排到队首。
/// </summary>
internal sealed class SendGate
{
    private static readonly ConcurrentDictionary<string, SendGate> Gates = new();

    private readonly object _sync = new();
    private readonly SendPacer _pacer = new();
    private readonly LinkedList<TaskCompletionSource> _queue = new();
    private readonly Func<long> _clock; //毫秒刻度
    private readonly Func<TimeSpan, Task> _delay;
    private readonly string _label; //日志里认这一组用，不含密钥
    private bool _pumping;

    internal SendGate(string label, Func<long>? clock = null, Func<TimeSpan, Task>? delay = null)
    {
        _label = label;
        _clock = clock ?? (() => Environment.TickCount64);
        _delay = delay ?? (wait => Task.Delay(wait));
    }

    /// <summary>
    /// 取这一组的闸，同组共用一个
    /// </summary>
    /// <param name="endpoint">端点地址</param>
    /// <param name="apiKey">密钥</param>
    /// <param name="modelId">请求里的模型名</param>
    /// <returns>这一组的闸</returns>
    public static SendGate For(string endpoint, string apiKey, string modelId) =>
        Gates.GetOrAdd($"{endpoint}\n{apiKey}\n{modelId}", _ => new SendGate($"{endpoint} · {modelId}"));

    /// <summary>当前间隔（诊断与测试用）</summary>
    internal TimeSpan Interval
    {
        get
        {
            lock (_sync) return _pacer.Interval;
        }
    }

    /// <summary>
    /// 等到轮到这条请求发
    /// </summary>
    /// <param name="isRetry">被打回后的重试：排队首</param>
    /// <param name="token">取消时出队</param>
    /// <returns>可以发了</returns>
    public Task WaitTurnAsync(bool isRetry, CancellationToken token)
    {
        TaskCompletionSource turn;
        LinkedListNode<TaskCompletionSource> node;
        lock (_sync)
        {
            if (_queue.Count == 0 && _pacer.Claim(_clock()) == TimeSpan.Zero) return Task.CompletedTask;

            turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            node = isRetry ? _queue.AddFirst(turn) : _queue.AddLast(turn);
            if (!_pumping)
            {
                _pumping = true;
                _ = PumpAsync();
            }
        }

        return token.CanBeCanceled ? AwaitTurnAsync(turn, node, token) : turn.Task;
    }

    private async Task AwaitTurnAsync(TaskCompletionSource turn, LinkedListNode<TaskCompletionSource> node,
        CancellationToken token)
    {
        await using CancellationTokenRegistration registration = token.Register(() =>
        {
            lock (_sync)
            {
                if (node.List != null) _queue.Remove(node);
            }

            turn.TrySetCanceled(token);
        });
        await turn.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 记一次 429
    /// </summary>
    /// <param name="sentAt">这条请求发出时的刻度，取自 <see cref="Now"/></param>
    /// <param name="retryAfter">服务端给的等待；没有为 null</param>
    /// <param name="isRetry">撞的这次本身是重试</param>
    public void OnRateLimited(long sentAt, TimeSpan? retryAfter, bool isRetry = false)
    {
        lock (_sync)
        {
            if (_pacer.OnRateLimited(sentAt, _clock(), retryAfter, isRetry))
                Log.Debug($"Send gate '{_label}' rate limited, interval -> {_pacer.Interval.TotalSeconds:0.#}s.");
        }
    }

    /// <summary>
    /// 记一次成功
    /// </summary>
    public void OnSuccess()
    {
        lock (_sync)
        {
            if (_pacer.OnSuccess()) Log.Debug($"Send gate '{_label}' opened.");
        }
    }

    /// <summary>当前刻度，给调用方记发出时间</summary>
    public long Now => _clock();

    private async Task PumpAsync()
    {
        while (true)
        {
            TaskCompletionSource? next = null;
            TimeSpan wait;
            lock (_sync)
            {
                if (_queue.Count == 0)
                {
                    _pumping = false;
                    return;
                }

                wait = _pacer.Claim(_clock());
                if (wait == TimeSpan.Zero)
                {
                    next = _queue.First!.Value;
                    _queue.RemoveFirst();
                }
            }

            // 取消的在取消时就出了队；刚出队就被取消的，这个发送位空过去
            if (next != null) next.TrySetResult();
            else await _delay(wait).ConfigureAwait(false);
        }
    }
}
