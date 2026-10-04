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
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Delivery;

/// <summary>一封信最后怎么到的</summary>
public enum EDeliveryOutcome
{
    /// <summary>插进那一轮并被取走，随那次模型调用落盘</summary>
    Consumed,

    /// <summary>落进历史并起了唤醒轮</summary>
    Written,

    /// <summary>历史里已经是这一封，没再叫醒</summary>
    Unchanged,

    /// <summary>收信人或内容不在了，作罢</summary>
    Missing,
}

/// <summary>
/// 送信（ADR 0062）：收信人醒着（一轮在跑）就插进那一轮，下一次模型调用前取走；
/// 闲着、或那一轮没取走就收了，撤回来落进历史再叫醒。
///
/// 被取走的随那次调用落盘，不再追加——判据是注入队列撤不撤得回，不靠比对历史。
/// 不设上限地等：那一轮总会结束
/// </summary>
public sealed class SessionDelivery
{
    private readonly ISessionDeliveryHost _host;

    /// <summary>应用里的那一个</summary>
    public static SessionDelivery Instance { get; } = new(new AppHost());

    internal SessionDelivery(ISessionDeliveryHost host)
    {
        _host = host;
    }

    /// <summary>
    /// 把信送到
    /// </summary>
    /// <param name="letter">信</param>
    /// <param name="onWaiting">第一次撞上收信人在跑时调（界面据此显示「等对方收」）；不需要为 null</param>
    /// <returns>怎么到的</returns>
    public async Task<EDeliveryOutcome> DeliverAsync(SessionLetter letter, Action? onWaiting = null)
    {
        string sessionId = letter.SessionId;
        ICharacterRunner? injectedInto = null; //插进去了的那个执行者，撤回要找同一个
        ChatMessage? injected = null;
        bool waited = false;
        while (true)
        {
            // 那一轮还在跑时就已取走：不必等它收完才算送到
            if (injectedInto != null && !injectedInto.PendingInjections.Any(x => ReferenceEquals(x, injected)))
                return EDeliveryOutcome.Consumed;

            if (injectedInto != null && !_host.IsBusy(sessionId))
            {
                IReadOnlyCollection<ChatMessage> withdrawn =
                    await injectedInto.CancelInjectionsAsync([injected!]).ConfigureAwait(false);
                if (withdrawn.Count == 0) return EDeliveryOutcome.Consumed;
                injectedInto = null;
            }

            if (TryWrite(letter, waitForIdle: true) is { } written)
            {
                if (written == ELetterWrite.Written)
                {
                    // 不等唤醒轮跑完：那是一整轮，送信方的收尾（摘「待回」、放闸）不该陪着等。唤醒自己兜住异常
                    _ = _host.WakeAsync(sessionId, letter.Cause);
                    return EDeliveryOutcome.Written;
                }

                return written == ELetterWrite.Unchanged ? EDeliveryOutcome.Unchanged : EDeliveryOutcome.Missing;
            }

            if (!waited)
            {
                waited = true;
                onWaiting?.Invoke();
            }

            if (injectedInto == null) (injectedInto, injected) = await TryInjectAsync(letter).ConfigureAwait(false);
            await Task.Delay(_host.BusyRetryInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 不等、不插、不唤醒，直接落进历史（应用退出时用：进程马上就没了）
    /// </summary>
    /// <param name="letter">信</param>
    public void WriteNow(SessionLetter letter) => TryWrite(letter, waitForIdle: false);

    // 收信人在跑时返回 null
    private ELetterWrite? TryWrite(SessionLetter letter, bool waitForIdle)
    {
        try
        {
            lock (SessionHistoryLocks.For(letter.SessionId))
            {
                if (_host.Load(letter.SessionId) is not { } session) return ELetterWrite.Missing;
                // 在跑时写它的历史会与那一轮逐次调用的落盘交错
                if (waitForIdle && _host.IsBusy(letter.SessionId)) return null;
                return letter.Write(session);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Write letter failed: session={letter.SessionId} cause={letter.Cause}: {e}");
            return ELetterWrite.Missing;
        }
    }

    private async Task<(ICharacterRunner?, ChatMessage?)> TryInjectAsync(SessionLetter letter)
    {
        try
        {
            if (_host.Load(letter.SessionId) is not { } session) return (null, null);
            ICharacterRunner runner = _host.RunnerOf(session);
            if (letter.Compose(session) is not { } message) return (null, null);
            return await runner.TryInjectAsync([message]).ConfigureAwait(false) ? (runner, message) : (null, null);
        }
        catch (Exception e)
        {
            Log.Warning($"Inject letter failed: session={letter.SessionId} cause={letter.Cause}: {e.Message}");
            return (null, null);
        }
    }

    private sealed class AppHost : ISessionDeliveryHost
    {
        public TimeSpan BusyRetryInterval => TimeSpan.FromSeconds(5);

        public ChatSession? Load(string sessionId) => SessionManager.Instance.Load(sessionId);

        public bool IsBusy(string sessionId) => SessionManager.Instance.Running.IsBusy(sessionId);

        public ICharacterRunner RunnerOf(ChatSession session) => session.Runner;

        public Task WakeAsync(string sessionId, string cause) => SessionWakeTurn.RunAsync(sessionId, cause);
    }
}
