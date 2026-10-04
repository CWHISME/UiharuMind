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

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 单聊的送法：会话正在跑就插进那一轮，醒着就及时收到；闲着（或那一轮没消费就收了）时
/// 把结果追加进它的历史，再起一轮唤醒轮。
///
/// 唤醒<b>不计</b>后台子代理那条连续唤醒上限：任务是用户批过的一条命令，跑完了就该有人接着看
/// </summary>
public sealed class SessionReportSink : IBackgroundTaskReportSink
{
    private readonly ISessionReportHost _host;

    /// <summary>无状态，全进程共用一份</summary>
    public static SessionReportSink Instance { get; } = new(new AppHost());

    internal SessionReportSink(ISessionReportHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public async Task DeliverAsync(BackgroundTaskOutcome outcome)
    {
        string sessionId = outcome.Task.OwnerSessionId;
        ChatMessage message = BackgroundTaskReport.BuildMessage(outcome);

        // 会话正在跑时写它的历史会与那一轮的落盘交错，改插进那一轮；它收了还没消费的撤回来，改走追加。
        // 消费了的随那一轮落盘，带着任务编号，追加时按编号认出来就不再写。不设上限：那一轮总会结束
        ICharacterRunner? injectedInto = null;
        EAppendResult result;
        while (true)
        {
            if (injectedInto != null && !_host.IsBusy(sessionId))
            {
                await injectedInto.CancelInjectionsAsync([message]).ConfigureAwait(false);
                injectedInto = null;
            }

            result = TryAppend(sessionId, outcome.Task.Id, message, waitForIdle: true);
            if (result != EAppendResult.Busy) break;

            injectedInto ??= await TryInjectAsync(sessionId, message).ConfigureAwait(false);
            await Task.Delay(_host.BusyRetryInterval).ConfigureAwait(false);
        }

        if (result == EAppendResult.Appended)
        {
            await _host.WakeAsync(sessionId, $"background task {outcome.Task.Id}").ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void DeliverOnShutdown(BackgroundTaskOutcome outcome) =>
        TryAppend(outcome.Task.OwnerSessionId, outcome.Task.Id, BackgroundTaskReport.BuildMessage(outcome),
            waitForIdle: false);

    private enum EAppendResult
    {
        Appended,
        AlreadyThere,
        Busy,
        Missing,
    }

    // 插进正在跑的那一轮，插进去了返回那个执行者（撤回要找同一个）
    private async Task<ICharacterRunner?> TryInjectAsync(string sessionId, ChatMessage message)
    {
        try
        {
            if (_host.Load(sessionId) is not { } session) return null;
            ICharacterRunner runner = _host.RunnerOf(session);
            return await runner.TryInjectAsync([message]).ConfigureAwait(false) ? runner : null;
        }
        catch (Exception e)
        {
            Log.Warning($"Inject background task report failed: session={sessionId}: {e.Message}");
            return null;
        }
    }

    // 按任务编号幂等:退出收尾与正常送达可能都走到这里,同一个任务只留一条
    private EAppendResult TryAppend(string sessionId, string taskId, ChatMessage message, bool waitForIdle)
    {
        try
        {
            lock (SessionHistoryLocks.For(sessionId))
            {
                ChatSession? session = _host.Load(sessionId);
                if (session == null) return EAppendResult.Missing;
                if (session.History.Any(x => ChatMessageAnnotations.ReadBackgroundTaskReportId(x) == taskId))
                    return EAppendResult.AlreadyThere;
                if (waitForIdle && _host.IsBusy(sessionId)) return EAppendResult.Busy;

                int from = session.History.Count;
                session.History.Add(message);
                session.SaveAppended(from);
                return EAppendResult.Appended;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Deliver background task report failed: session={sessionId}: {e}");
            return EAppendResult.Missing;
        }
    }

    private sealed class AppHost : ISessionReportHost
    {
        public TimeSpan BusyRetryInterval => TimeSpan.FromSeconds(5); //与后台子代理交回同一口径

        public ChatSession? Load(string sessionId) => SessionManager.Instance.Load(sessionId);

        public bool IsBusy(string sessionId) => SessionManager.Instance.Running.IsBusy(sessionId);

        public ICharacterRunner RunnerOf(ChatSession session) => session.Runner;

        public Task WakeAsync(string sessionId, string cause) => SessionWakeTurn.RunAsync(sessionId, cause);
    }
}
