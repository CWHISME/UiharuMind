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

            // 下标与追加一次拿到：分两步的话，中间被别的写方（执行轮、群投递）插一条，
            // 落盘起点就指到别人那条上，重开会话会多一条重复气泡
            int from = session.History.AddReturningIndex(Compose(session));
            session.SaveAppended(from);
            return ELetterWrite.Written;
        }

        // 送达层去重键：同任务同正文的历史里已有一封就不再写。Write 里的
        // 「同任务即不写」更严（正文改了也不更新），保留它当旧语义兜底
        public override string? DedupeKey => outcome.Task.Id;

        public override string? KeyOf(ChatMessage message) =>
            ChatMessageAnnotations.ReadBackgroundTaskReportId(message) is { Length: > 0 } id ? id : null;
    }
}
