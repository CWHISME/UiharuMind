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
using UiharuMind.Core.AI.Execution.Delivery;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 单聊的送法：交给 <see cref="SessionDelivery"/>——会话醒着就插进那一轮，闲着才追加再唤醒
/// </summary>
public sealed class SessionReportSink : IBackgroundTaskReportSink
{
    private readonly SessionDelivery _delivery;

    /// <summary>无状态，全进程共用一份</summary>
    public static SessionReportSink Instance { get; } = new(SessionDelivery.Instance);

    internal SessionReportSink(SessionDelivery delivery)
    {
        _delivery = delivery;
    }

    /// <inheritdoc />
    public Task DeliverAsync(BackgroundTaskOutcome outcome) => _delivery.DeliverAsync(new Letter(outcome));

    /// <inheritdoc />
    public void DeliverOnShutdown(BackgroundTaskOutcome outcome) => _delivery.WriteNow(new Letter(outcome));

    private sealed class Letter(BackgroundTaskOutcome outcome) : SessionLetter
    {
        private ChatMessage? _message;

        public override string SessionId => outcome.Task.OwnerSessionId;

        public override string Cause => $"background task {outcome.Task.Id}";

        // 读日志尾巴有 IO，组装一次就够：结局不会再变
        public override ChatMessage Compose(ChatSession session) => _message ??= BackgroundTaskReport.BuildMessage(outcome);

        // 按任务编号幂等：退出收尾与正常送达可能都走到这里，同一个任务只留一条
        public override ELetterWrite Write(ChatSession session)
        {
            if (session.History.Any(x => ChatMessageAnnotations.ReadBackgroundTaskReportId(x) == outcome.Task.Id))
                return ELetterWrite.Unchanged;

            int from = session.History.Count;
            session.History.Add(Compose(session));
            session.SaveAppended(from);
            return ELetterWrite.Written;
        }
    }
}
