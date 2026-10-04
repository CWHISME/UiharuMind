/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 群成员的送法：结果作为附注交给群调度——他正在说就插进那一轮，否则单独叫他在群里开口一次，说完照常进群。
/// 他已经不在群里了就退回单聊的送法
/// </summary>
public sealed class GroupMemberReportSink : IBackgroundTaskReportSink
{
    /// <summary>无状态，全进程共用一份</summary>
    public static GroupMemberReportSink Instance { get; } = new();

    private GroupMemberReportSink()
    {
    }

    /// <inheritdoc />
    public async Task DeliverAsync(BackgroundTaskOutcome outcome)
    {
        bool delivered = await GroupChatCoordinator.Instance
            .NotifyMemberAsync(outcome.Task.OwnerSessionId, BackgroundTaskReport.BuildMessage(outcome))
            .ConfigureAwait(false);
        if (!delivered) await SessionReportSink.Instance.DeliverAsync(outcome).ConfigureAwait(false);
    }

    // 附注只在内存里,退出时直接落进他自己的会话,下次轮到他时历史里看得到
    /// <inheritdoc />
    public void DeliverOnShutdown(BackgroundTaskOutcome outcome) => SessionReportSink.Instance.DeliverOnShutdown(outcome);
}
