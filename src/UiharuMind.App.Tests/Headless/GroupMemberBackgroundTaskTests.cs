using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 成员名下起了后台任务：右栏名字旁挂蓝点（悬停看任务说明），任务跑完/叫停后蓝点消失。
/// 与「发言中/私聊中/等审批」不同，后台任务是一段更长命的活，成员闲着或说着话都可以挂着，
/// 所以不占右上角那三个互斥的状态位，挂在名字行
/// </summary>
[Collection(HeadlessCollection.Name)]
public class GroupMemberBackgroundTaskTests : IDisposable
{
    // Shell 解析在 Core 内部（ShellExecutorFactory 只对 Core.Tests 开放），App.Tests 起真进程直接给路径。
    // 本仓开发/测试机是 macOS，/bin/bash 恒在
    private const string ShellBinary = "/bin/bash";

    private readonly List<string> _ownedGroups = [];
    private readonly List<string> _ownedTasks = [];

    public void Dispose()
    {
        foreach (string taskId in _ownedTasks) BackgroundTaskRegistry.Stop(taskId);
        foreach (string groupId in _ownedGroups)
        {
            foreach (ChatSessionMeta member in SessionManager.Instance.GetGroupMembers(groupId))
                SessionManager.Instance.Delete(member.SessionId);
            SessionManager.Instance.Delete(groupId);
        }
    }

    /// <summary>任务在跑：那位成员挂上蓝点，悬停提示里有任务说明；别人不挂</summary>
    [Fact]
    public void MemberWithRunningBackgroundTask_ShowsDot()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession group = NewGroup();
            (Window window, GroupMembersViewData members, RecordingMessageService messages) = ShowPanel(group);
            try
            {
                string memberId = group.GroupMemberSessionIds[0];
                StartTask(memberId, "后台构建");
                members.RefreshBackgroundTasks();

                Assert.True(members.Members[0].HasBackgroundTask);
                Assert.Contains("后台构建", members.Members[0].BackgroundTaskTip);
                Assert.False(members.Members[1].HasBackgroundTask);
                Assert.NotNull(FindVisibleTaskDot(window));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>任务结束（用户叫停也算）：蓝点消失，提示清空</summary>
    [Fact]
    public void TaskStopped_DotDisappears()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession group = NewGroup();
            (Window window, GroupMembersViewData members, RecordingMessageService messages) = ShowPanel(group);
            try
            {
                string memberId = group.GroupMemberSessionIds[0];
                BackgroundTask task = StartTask(memberId, "后台构建");
                members.RefreshBackgroundTasks();
                Assert.True(members.Members[0].HasBackgroundTask);

                Assert.True(BackgroundTaskRegistry.Stop(task.Id));
                // 停的是进程树；注册表要等进程真正退出、输出收完（排水上限 2s）才摘掉任务，轮询等它落地
                for (int i = 0; i < 100 && BackgroundTaskRegistry.RunningOf(memberId).Count > 0; i++)
                    Thread.Sleep(50);
                Assert.Empty(BackgroundTaskRegistry.RunningOf(memberId));
                members.RefreshBackgroundTasks();

                Assert.False(members.Members[0].HasBackgroundTask);
                Assert.Equal("", members.Members[0].BackgroundTaskTip);
                Assert.Null(FindVisibleTaskDot(window));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private (Window, GroupMembersViewData, RecordingMessageService) ShowPanel(ChatSession group)
    {
        RecordingMessageService messages = new() { ConfirmResult = false };
        GroupMembersViewData members = new(group, new GroupChangePrompts(messages));
        GroupMembersPanel panel = new() { Width = 300, Height = 600, DataContext = members };
        Window window = new() { Width = 400, Height = 700, Content = panel };
        window.Show();
        Pump(window);
        return (window, members, messages);
    }

    /// <summary>
    /// 后台任务蓝点：status-dot 且父容器是 Grid（名字行）的那一颗。
    /// 右上角三个互斥状态（发言/私聊/等审批）的圆点都在 StackPanel 里，用父容器区分开
    /// </summary>
    private static Border? FindVisibleTaskDot(Window window) => window.GetVisualDescendants()
        .OfType<Border>()
        .FirstOrDefault(b => b.Classes.Contains("status-dot") && Equals(b.Tag, "Progress")
                             && b.Parent is Grid && b.IsEffectivelyVisible);

    private static void Pump(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private ChatSession NewGroup()
    {
        ChatSession group = GroupChatSessions.Create("g-bg-task", false,
            [new CharacterData { CharacterName = "A" }, new CharacterData { CharacterName = "B" }],
            null, null, new GroupSchedule(EGroupScheduleMode.Parallel, EGroupStopPolicy.Conservative, 0));
        _ownedGroups.Add(group.SessionId);
        return group;
    }

    private static BackgroundTask StartTask(string memberId, string description)
    {
        BackgroundTaskLaunch launch = new(ShellBinary, Path.GetTempPath(), new Dictionary<string, string?>(),
            Directory.CreateTempSubdirectory("bg-dot").FullName);
        return BackgroundTaskRegistry.Start(memberId, "sleep 30", description, TimeSpan.FromMinutes(1), launch,
            new NoopSink());
    }

    private sealed class NoopSink : IBackgroundTaskReportSink
    {
        public Task DeliverAsync(BackgroundTaskOutcome outcome) => Task.CompletedTask;

        public void DeliverOnShutdown(BackgroundTaskOutcome outcome)
        {
        }
    }
}
