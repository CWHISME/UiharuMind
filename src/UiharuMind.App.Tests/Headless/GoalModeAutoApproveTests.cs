using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Single;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 目标模式的「自动同意审批」：勾上要先过危险度确认，取消就回弹；确认过才随开始/更新落到控制器；
/// 正在进行的目标模式再开设置时原样回显，且不再问第二遍
/// </summary>
[Collection(HeadlessCollection.Name)]
public class GoalModeAutoApproveTests
{
    [Fact]
    public void Checking_AsksAboutTheRisk_DecliningRevertsTheBox()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = NewSession();
            RecordingMessageService messages = new() { ConfirmResult = false };
            GoalModeController goalMode = NewGoalMode();
            using GoalModeViewData viewData = new(session, messages, goalMode);
            (Window window, CheckBox box) = ShowSetup(viewData);
            try
            {
                box.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.Single(messages.Confirms); //弹了二次提示
                Assert.Equal(Loc.Text(LangKey.GoalModeAutoApproveConfirm), messages.LastConfirm);
                Assert.False(viewData.AutoApproveApprovals);
                Assert.False(box.IsChecked); //界面跟着回弹
                Assert.Empty(goalMode.ActiveStatuses()); //没开始，什么也没落下
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Confirming_AndStarting_CarriesTheFlagIntoTheController()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = NewSession();
            RecordingMessageService messages = new() { ConfirmResult = true };
            GoalModeController goalMode = NewGoalMode();
            using GoalModeViewData viewData = new(session, messages, goalMode);
            (Window window, CheckBox box) = ShowSetup(viewData);
            try
            {
                box.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.True(viewData.AutoApproveApprovals);
                Assert.True(box.IsChecked);

                viewData.StartCommand.Execute(null); //相当于点「开始目标模式」
                Dispatcher.UIThread.RunJobs();

                Assert.True(goalMode.StatusOf(session.SessionId)!.AutoApproveApprovals);
                Assert.True(goalMode.IsAutoApprove(session.SessionId));
            }
            finally
            {
                window.Close();
                goalMode.End(session.SessionId);
            }
        });
    }

    /// <summary>正在进行的目标模式再开设置：勾选状态原样回显，回填不算用户勾选，不弹第二遍确认</summary>
    [Fact]
    public void ReopeningWhileActive_ShowsTheFlag_WithoutAskingAgain()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = NewSession();
            GoalModeController goalMode = NewGoalMode();
            Assert.True(goalMode.Start(session, new GoalModeSettings("目标", AutoApproveApprovals: true)));
            RecordingMessageService messages = new();
            using GoalModeViewData viewData = new(session, messages, goalMode);
            (Window window, CheckBox box) = ShowSetup(viewData);
            try
            {
                Assert.True(viewData.AutoApproveApprovals);
                Assert.True(box.IsChecked);
                Assert.False(viewData.HasChanges);
                Assert.Empty(messages.Confirms);
            }
            finally
            {
                window.Close();
                goalMode.End(session.SessionId);
            }
        });
    }

    /// <summary>结束后再开设置弹窗：目标与调度规则原样回显——结束目标模式不该把设过的内容清掉</summary>
    [Fact]
    public void ReopeningAfterEnd_ShowsTheLastGoalAndRules()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = NewSession();
            GoalModeController goalMode = NewGoalMode();
            GoalModeScheduleRule rule = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(45));
            Assert.True(goalMode.Start(session, new GoalModeSettings("写一份方案", [rule], true)));
            goalMode.End(session.SessionId);

            RecordingMessageService messages = new();
            using GoalModeViewData viewData = new(session, messages, goalMode);
            (Window window, _) = ShowSetup(viewData);
            try
            {
                Assert.Equal("写一份方案", viewData.Goal);
                Assert.Equal(rule, Assert.Single(viewData.Rules).ToRule());
                Assert.True(viewData.AutoApproveApprovals);
                Assert.False(viewData.IsActive); //没在进行：按钮区是「开始/取消」
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>进行中改勾选：更新按钮置灰解除，「更新」把它带进正在进行的目标模式</summary>
    [Fact]
    public void CheckingWhileActive_UpdateCarriesTheFlag()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = NewSession();
            RecordingMessageService messages = new() { ConfirmResult = true };
            GoalModeController goalMode = NewGoalMode();
            Assert.True(goalMode.Start(session, new GoalModeSettings("目标")));
            using GoalModeViewData viewData = new(session, messages, goalMode);
            (Window window, CheckBox box) = ShowSetup(viewData);
            try
            {
                Assert.False(viewData.AutoApproveApprovals);
                Assert.False(viewData.HasChanges);

                box.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(viewData.HasChanges); //没按更新就不算改

                viewData.UpdateCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.True(goalMode.IsAutoApprove(session.SessionId));
                Assert.False(viewData.HasChanges);
            }
            finally
            {
                window.Close();
                goalMode.End(session.SessionId);
            }
        });
    }

    /// <summary>
    /// 唤醒轮的审批被自动同意：画出来的卡片就地收成已同意，从待决清单里摘掉——
    /// 不然窗口开着的人看到一张永远等在原地的卡，像还没人应
    /// </summary>
    [Fact]
    public void AutoApprovedTurn_FoldsTheCardsOutOfThePendingList()
    {
        HeadlessUi.Run(() =>
        {
            List<ConversationItemBase> items = [];
            ConversationTranscript transcript =
                new(items, () => new TextConversationItem(false));
            ToolApprovalRequestContent request =
                new("r1", new FunctionCallContent("c1", "Write", new Dictionary<string, object?>()));
            transcript.Apply(request);
            Assert.Single(transcript.PendingApprovals);

            // 与运行器同一路线:先由无人值守策略给出回应,再按它把卡片收掉
            IReadOnlyList<ChatMessage> responses =
                GoalModeApproval.Create("session")([request]).GetAwaiter().GetResult();
            transcript.ResolveApprovalsElsewhere([request], responses, LangKey.AgentApprovalResolvedAuto);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(transcript.PendingApprovals);
            ApprovalRequestItem card = Assert.Single(items.OfType<ApprovalRequestItem>());
            Assert.True(card.IsResolved);
            Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedAuto), card.ResolvedText);
            // 卡片上那一行就是真发出去的那条回应:显示与事实不会对不上
            Assert.Equal(GoalModeApproval.Reason, card.Response.Result.Contents
                .OfType<ToolApprovalResponseContent>().Single().Reason);
        });
    }

    private static ChatSession NewSession() =>
        new("goal-auto", new CharacterData { CharacterId = "goal-auto" }) { IsTransient = true };

    private static GoalModeController NewGoalMode() =>
        new(wakeTurn: (_, _, _) => Task.CompletedTask, primary: true);

    private static (Window Window, CheckBox Box) ShowSetup(GoalModeViewData viewData)
    {
        GoalModeSetupWindow window = new() { DataContext = viewData };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.AutoApproveCheck);
    }
}
