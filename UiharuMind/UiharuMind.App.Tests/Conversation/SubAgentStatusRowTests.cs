/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 输入区上方那条子代理状态：<b>派出去了就得有一行</b>。
///
/// 从前只有「等审批」那一档有横幅，子代理闷头跑着、或者跑完了压着等派活者空闲时，
/// 界面上一个字都没有——用户看到的是「派出去就没消息了」「回执丢了」。
/// </summary>
public class SubAgentStatusRowTests
{
    [Fact]
    public void Collect_ShowsARunningRowWhileTheDelegationIsStillWorking()
    {
        ChatSession sub = new() { SessionId = "status-sub", ParentSessionId = "status-parent" };
        TaskCompletionSource gate = new();
        BackgroundSubAgentDispatcher.Dispatch(sub, async _ =>
        {
            await gate.Task;
            return "结论";
        });

        List<SubAgentStatusViewData> rows = SubAgentStatusViewData.Collect("status-parent");

        SubAgentStatusViewData row = Assert.Single(rows);
        Assert.Equal(ESubAgentStatusKind.Running, row.Kind);
        Assert.Equal(1, row.Count);
        Assert.Equal("status-sub", row.SubSessionIds[0]);
        Assert.True(row.IsSpinning); //在跑才转圈,等人与等时机那两档不转

        gate.SetResult();
    }

    [Fact]
    public void Collect_SaysNothingAboutSomeoneElsesDelegations()
    {
        Assert.Empty(SubAgentStatusViewData.Collect("nobody-delegated-here"));
    }
}
