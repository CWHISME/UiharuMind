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
/// 单聊的送法：等会话空闲时把结果追加进它的历史，再起一轮唤醒轮。
///
/// 唤醒<b>不计</b>后台子代理那条连续唤醒上限：任务是用户批过的一条命令，跑完了就该有人接着看
/// </summary>
public sealed class SessionReportSink : IBackgroundTaskReportSink
{
    // 会话正忙时的重试间隔,与后台子代理交回同一口径
    private static readonly TimeSpan BusyRetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>无状态，全进程共用一份</summary>
    public static SessionReportSink Instance { get; } = new();

    private SessionReportSink()
    {
    }

    /// <inheritdoc />
    public async Task DeliverAsync(BackgroundTaskOutcome outcome)
    {
        string sessionId = outcome.Task.OwnerSessionId;
        ChatMessage message = BackgroundTaskReport.BuildMessage(outcome);

        // 会话正在跑时写它的历史会与那一轮的落盘交错,等它闲下来。不设上限:那一轮总会结束
        EAppendResult result;
        while ((result = TryAppend(sessionId, outcome.Task.Id, message, waitForIdle: true)) == EAppendResult.Busy)
        {
            await Task.Delay(BusyRetryInterval).ConfigureAwait(false);
        }

        if (result == EAppendResult.Appended)
        {
            await SessionWakeTurn.RunAsync(sessionId, $"background task {outcome.Task.Id}").ConfigureAwait(false);
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

    // 按任务编号幂等:退出收尾与正常送达可能都走到这里,同一个任务只留一条
    private static EAppendResult TryAppend(string sessionId, string taskId, ChatMessage message, bool waitForIdle)
    {
        try
        {
            lock (SessionHistoryLocks.For(sessionId))
            {
                ChatSession? session = SessionManager.Instance.Load(sessionId);
                if (session == null) return EAppendResult.Missing;
                if (session.History.Any(x => ChatMessageAnnotations.ReadBackgroundTaskReportId(x) == taskId))
                    return EAppendResult.AlreadyThere;
                if (waitForIdle && SessionManager.Instance.Running.IsBusy(sessionId)) return EAppendResult.Busy;

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
}
