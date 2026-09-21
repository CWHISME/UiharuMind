using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 定时任务侧栏的三档选择。任务到点是无人值守跑的，所以「界面上选的那一档有没有真的落到任务上」
/// 没有任何运行期反馈可看——只能靠测试钉住。
///
/// 调度后端换成替身：真的 <c>InProcessSchedulerBackend</c> 构造函数就会读写用户存档目录里的
/// <c>ScheduledAgentTasks.json</c>，测试不能碰那个文件。
/// </summary>
public class ScheduledTaskListModelTests
{
    private static ScheduledAgentTask NewTask(EAgentPermissionMode mode = EAgentPermissionMode.AutoEdit,
        EScheduledTaskStatus status = EScheduledTaskStatus.Pending, string id = "t1")
    {
        return new ScheduledAgentTask
        {
            TaskId = id,
            DisplayName = $"任务 {id}",
            FireAt = DateTimeOffset.Now.AddMinutes(30),
            PermissionMode = mode,
            Status = status,
        };
    }

    /// <summary>同步执行的 post：测试里不该有跨线程调度</summary>
    private static ScheduledTaskListModel Create(FakeScheduler scheduler)
        => new(null, action => action(), scheduler);

    //================= 显示 =================

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly, 0)]
    [InlineData(EAgentPermissionMode.AutoEdit, 1)]
    [InlineData(EAgentPermissionMode.FullAuto, 2)]
    public void Item_ShowsTheTaskPermissionMode(EAgentPermissionMode mode, int expectedIndex)
    {
        FakeScheduler scheduler = new(NewTask(mode));

        ScheduledTaskListModel model = Create(scheduler);

        Assert.Equal(expectedIndex, model.Tasks[0].PermissionModeIndex);
    }

    /// <summary>三档的文案不能是同一句话——这是"标签跟着档位走"唯一不依赖语言的判据</summary>
    [Fact]
    public void PermissionText_DiffersPerMode()
    {
        FakeScheduler scheduler = new(
            NewTask(EAgentPermissionMode.ReadOnly, id: "a"),
            NewTask(EAgentPermissionMode.AutoEdit, id: "b"),
            NewTask(EAgentPermissionMode.FullAuto, id: "c"));

        ScheduledTaskListModel model = Create(scheduler);

        string[] labels = model.Tasks.Select(x => x.PermissionText).ToArray();
        Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
        Assert.Equal(3, labels.Distinct().Count());
    }

    /// <summary>没设过档位的任务（含旧存档里的）在界面上就是"自动编辑"，不是"只读"</summary>
    [Fact]
    public void TaskWithoutAnExplicitMode_ShowsAutoEdit()
    {
        FakeScheduler scheduler = new(new ScheduledAgentTask { TaskId = "t1", DisplayName = "旧任务" });

        ScheduledTaskListModel model = Create(scheduler);

        Assert.Equal((int)EAgentPermissionMode.AutoEdit, model.Tasks[0].PermissionModeIndex);
    }

    //================= 可改与不可改 =================

    [Theory]
    [InlineData(EScheduledTaskStatus.Pending, true)]
    [InlineData(EScheduledTaskStatus.Missed, true)]
    [InlineData(EScheduledTaskStatus.Running, false)]
    [InlineData(EScheduledTaskStatus.Completed, false)]
    [InlineData(EScheduledTaskStatus.Failed, false)]
    [InlineData(EScheduledTaskStatus.Cancelled, false)]
    public void PermissionIsEditable_OnlyBeforeTheRun(EScheduledTaskStatus status, bool expected)
    {
        FakeScheduler scheduler = new(NewTask(status: status));

        ScheduledTaskListModel model = Create(scheduler);

        Assert.Equal(expected, model.Tasks[0].CanEditPermission);
    }

    //================= 写回 =================

    [Fact]
    public void ChangingThePermission_ReachesTheScheduler()
    {
        FakeScheduler scheduler = new(NewTask());
        ScheduledTaskListModel model = Create(scheduler);

        model.Tasks[0].PermissionModeIndex = (int)EAgentPermissionMode.FullAuto;

        Assert.Equal([("t1", EAgentPermissionMode.FullAuto)], scheduler.Applied);
    }

    /// <summary>
    /// 后端落盘后会回一次 OnTaskUpdated、列表随之重建。重建出来的条目<b>不能</b>再写一次——
    /// 否则每改一档就是一串写盘，改档位与"后端通知"之间形成一个来回。
    /// </summary>
    [Fact]
    public void RebuildingTheList_DoesNotWriteBack()
    {
        FakeScheduler scheduler = new(NewTask(EAgentPermissionMode.FullAuto));
        ScheduledTaskListModel model = Create(scheduler);

        model.Sync();
        scheduler.RaiseUpdated();

        Assert.Empty(scheduler.Applied);
    }

    /// <summary>改档位之后列表要显示改后的那一档（走的是后端通知那条路）</summary>
    [Fact]
    public void AfterTheChange_TheListShowsTheNewMode()
    {
        FakeScheduler scheduler = new(NewTask());
        ScheduledTaskListModel model = Create(scheduler);

        model.Tasks[0].PermissionModeIndex = (int)EAgentPermissionMode.ReadOnly;

        Assert.Equal((int)EAgentPermissionMode.ReadOnly, model.Tasks[0].PermissionModeIndex);
        Assert.Single(scheduler.Applied); //只写一次:通知引发的重建没有再写回来
    }

    [Fact]
    public void Dispose_DetachesFromTheScheduler()
    {
        FakeScheduler scheduler = new(NewTask());
        ScheduledTaskListModel model = Create(scheduler);

        model.Dispose();

        Assert.False(scheduler.HasSubscribers);
    }

    /// <summary>
    /// 调度后端替身：任务列表在内存里，<c>SetPermissionModeAsync</c> 记一笔并像真后端那样回一次通知。
    /// </summary>
    private sealed class FakeScheduler : ISchedulerBackend
    {
        private readonly List<ScheduledAgentTask> _tasks;

        public FakeScheduler(params ScheduledAgentTask[] tasks) => _tasks = tasks.ToList();

        /// <summary>收到的权限档改动</summary>
        public List<(string TaskId, EAgentPermissionMode Mode)> Applied { get; } = new();

        public bool HasSubscribers => OnTaskUpdated != null;

        public string BackendId => "fake";

        public event Action<ScheduledAgentTask>? OnTaskUpdated;

        public void RaiseUpdated()
        {
            if (_tasks.Count > 0) OnTaskUpdated?.Invoke(_tasks[0]);
        }

        public Task ScheduleAsync(ScheduledAgentTask task)
        {
            _tasks.Add(task);
            return Task.CompletedTask;
        }

        public Task CancelAsync(string taskId) => Task.CompletedTask;

        public Task RunNowAsync(string taskId) => Task.CompletedTask;

        public Task SetPermissionModeAsync(string taskId, EAgentPermissionMode mode)
        {
            Applied.Add((taskId, mode));
            ScheduledAgentTask? task = _tasks.FirstOrDefault(x => x.TaskId == taskId);
            if (task == null) return Task.CompletedTask;

            task.PermissionMode = mode;
            OnTaskUpdated?.Invoke(task); //真后端也这么干,列表因此会重建
            return Task.CompletedTask;
        }

        public IReadOnlyList<ScheduledAgentTask> GetTasks() => _tasks;
    }
}
