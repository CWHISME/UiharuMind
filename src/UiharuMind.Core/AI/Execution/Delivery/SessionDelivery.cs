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
    /// <summary>插进那一轮，随取走它的那次模型调用落进了历史</summary>
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
/// 插进去的那封落进了收信人历史才算送到（随取走它的那次模型调用落盘）；那一轮收了还在队列里、
/// 撤不动、或取走了却没落盘，都改走落盘。不设上限地等：那一轮总会结束
/// </summary>
public sealed class SessionDelivery
{
    private const int RecentScan = 200; //认插进去的那封落没落盘时，从历史末尾往前看多少条

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
            if (injected != null)
            {
                EInjected state = await CheckInjectedAsync(sessionId, injectedInto!, injected).ConfigureAwait(false);
                if (state == EInjected.Persisted) return EDeliveryOutcome.Consumed;
                if (state == EInjected.Pending)
                {
                    await Task.Delay(_host.BusyRetryInterval).ConfigureAwait(false);
                    continue;
                }

                injectedInto = null;
                injected = null;
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

            (injectedInto, injected) = await TryInjectAsync(letter).ConfigureAwait(false);
            await Task.Delay(_host.BusyRetryInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 不等、不插、不唤醒，直接落进历史（应用退出时用：进程马上就没了）
    /// </summary>
    /// <param name="letter">信</param>
    public void WriteNow(SessionLetter letter) => TryWrite(letter, waitForIdle: false);

    private enum EInjected
    {
        Pending, //还在队列里，或取走了还没随那次调用落盘，那一轮也还在跑
        Persisted, //已在收信人历史里
        Abandoned, //撤回来了、撤不动了，或取走了却没落进历史（那次调用没发出去就收了）：改走落盘
    }

    /// <summary>
    /// 插进去的那封此刻怎样了。离开队列不等于送到：它随<b>那次</b>模型调用结束才落盘，
    /// 中间进程没了就丢；那次调用没发出去（刚取走就被停）也就不会落盘。所以送到只认历史里有它
    /// </summary>
    private async Task<EInjected> CheckInjectedAsync(string sessionId, ICharacterRunner runner, ChatMessage message)
    {
        if (!runner.PendingInjections.Any(x => ReferenceEquals(x, message)))
        {
            // 先读忙闲再看历史：一轮的落盘总在它报空闲之前，读到空闲时该落的都已落了
            bool busy = _host.IsBusy(sessionId);
            if (IsInHistory(sessionId, message)) return EInjected.Persisted;
            return busy ? EInjected.Pending : EInjected.Abandoned;
        }

        if (_host.IsBusy(sessionId)) return EInjected.Pending;

        try
        {
            if ((await runner.CancelInjectionsAsync([message]).ConfigureAwait(false)).Count > 0) return EInjected.Abandoned;
        }
        catch (Exception e)
        {
            Log.Warning($"Withdraw letter failed: session={sessionId}: {e.Message}");
            return EInjected.Abandoned;
        }

        // 撤不回：要么刚被新起的一轮取走（下次按取走了看），要么执行者已经换掉、队列跟着没了
        return runner.PendingInjections.Any(x => ReferenceEquals(x, message)) ? EInjected.Abandoned : EInjected.Pending;
    }

    // 落盘的可能是框架重建的副本，按正文认；正文里带着任务编号或子会话标识，不会撞上别的
    private bool IsInHistory(string sessionId, ChatMessage message)
    {
        try
        {
            if (_host.Load(sessionId) is not { } session) return false;
            IList<ChatMessage> history = session.History;
            for (int i = history.Count - 1; i >= 0 && i >= history.Count - RecentScan; i--)
            {
                if (ReferenceEquals(history[i], message) || history[i].Text == message.Text) return true;
            }
        }
        catch (Exception)
        {
            //那一轮正往历史里追加，下次再看
        }

        return false;
    }

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
