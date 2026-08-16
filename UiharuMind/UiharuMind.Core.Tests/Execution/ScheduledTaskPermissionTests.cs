using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 定时任务的权限档：存档回落、执行时取的是任务上那一档、以及无人值守的审批收口。
///
/// 这三件事都是「不会编译报错的缺陷」：档位漏在硬编码里、旧存档被悄悄提档、
/// 审批请求在没人的时候挂死，屏幕上都看不出任何异常。
///
/// 不构造 <see cref="InProcessSchedulerBackend"/>——它的构造函数会读写
/// <c>ScheduledAgentTasks.json</c>，那是用户真实存档目录。因此这里只测它的两个静态面：
/// 会话装配（<c>CreateRunSession</c>）与审批策略（<c>DenyUnauthorizedApprovals</c>），
/// 两者都是纯的，且恰好是全部有争议的逻辑所在。
/// </summary>
public class ScheduledTaskPermissionTests
{
    private const string Root = "/tmp/uiharu-task-ws";

    private static ScheduledAgentTask NewTask(EAgentPermissionMode mode = EAgentPermissionMode.AutoEdit)
    {
        return new ScheduledAgentTask
        {
            DisplayName = "夜里提交仓库",
            Prompt = "commit the repo",
            WorkspacePath = Root,
            PermissionMode = mode,
            PreAuthorizedCommands = { "git commit*" },
        };
    }

    private static ToolApprovalRequestContent ShellRequest(string command = "rm -rf /")
    {
        return new ToolApprovalRequestContent("req-1", new FunctionCallContent("c1",
            CharacterRunnerFactory.ShellToolName, new Dictionary<string, object?> { ["command"] = command }));
    }

    private static ToolApprovalRequestContent EditRequest(string filePath = "/etc/hosts")
    {
        return new ToolApprovalRequestContent("req-2", new FunctionCallContent("c2", "Edit",
            new Dictionary<string, object?> { ["filePath"] = filePath }));
    }

    //================= 存档与回落 =================

    /// <summary>
    /// 权限档是后加的字段，<b>旧存档里没有它</b>。回落必须是 AutoEdit（加字段前写死的那一档），
    /// 不能是枚举的 default（ReadOnly）——那会让所有既有定时任务一夜之间连工作区内的写入都做不了。
    /// 读的是生产同一条路径：<see cref="SaveUtility.LoadFromString{T}"/> 配 <c>JsonOptions</c>。
    /// </summary>
    [Fact]
    public void OldArchiveWithoutPermissionMode_FallsBackToAutoEdit()
    {
        const string oldJson = """
                               [
                                 {
                                   "TaskId": "abc",
                                   "DisplayName": "旧任务",
                                   "FireAt": "2026-01-01T09:00:00+08:00",
                                   "Prompt": "do something",
                                   "WorkspacePath": "/tmp/ws",
                                   "PreAuthorizedCommands": [ "git status*" ],
                                   "MissedFirePolicy": 0,
                                   "Status": 0,
                                   "CreatedAt": "2025-12-31T09:00:00+08:00"
                                 }
                               ]
                               """;

        List<ScheduledAgentTask> tasks = SaveUtility.LoadFromString<List<ScheduledAgentTask>>(oldJson);

        ScheduledAgentTask task = Assert.Single(tasks);
        Assert.Equal(EAgentPermissionMode.AutoEdit, task.PermissionMode);
        Assert.Equal("旧任务", task.DisplayName); //其余字段照旧读到,不是整条读失败才"回落"成默认对象
        Assert.Equal(["git status*"], task.PreAuthorizedCommands);
    }

    /// <summary>整个文件读不动时 List 为空，那是另一码事——别把它和"缺字段"搞混</summary>
    [Fact]
    public void EmptyArchive_YieldsNoTasks()
    {
        Assert.Empty(SaveUtility.LoadFromString<List<ScheduledAgentTask>>("[]"));
    }

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void PermissionMode_SurvivesTheArchiveRoundTrip(EAgentPermissionMode mode)
    {
        string json = SaveUtility.SaveToString(new List<ScheduledAgentTask> { NewTask(mode) });

        List<ScheduledAgentTask> restored = SaveUtility.LoadFromString<List<ScheduledAgentTask>>(json);

        Assert.Equal(mode, Assert.Single(restored).PermissionMode);
    }

    //================= 执行时取的是任务上那一档 =================

    /// <summary>
    /// 曾经这里写死 <c>AutoEdit</c>，于是任务上选哪一档都没用。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void RunSession_TakesThePermissionModeFromTheTask(EAgentPermissionMode mode)
    {
        ChatSession session = InProcessSchedulerBackend.CreateRunSession(NewTask(mode));

        Assert.Equal((int)mode, session.PermissionModeIndex);
    }

    /// <summary>
    /// 存档被手改坏（档位是个枚举之外的数）时回落 AutoEdit，<b>不能</b>就这么传下去：
    /// 下游 <c>AgentBuildProfile</c> 用的是 <c>Clamp(0, 2)</c>，一个越界的大数会被悄悄
    /// 变成 FullAuto——坏数据不该是提权的路子。
    /// </summary>
    [Fact]
    public void RunSession_WithACorruptMode_FallsBackToAutoEditNotFullAuto()
    {
        ScheduledAgentTask task = NewTask();
        task.PermissionMode = (EAgentPermissionMode)7;

        ChatSession session = InProcessSchedulerBackend.CreateRunSession(task);

        Assert.Equal((int)EAgentPermissionMode.AutoEdit, session.PermissionModeIndex);
    }

    /// <summary>预授权与工作目录也要跟着走：三档之外，它们才是任务能真的干活的另一半</summary>
    [Fact]
    public void RunSession_CarriesWorkspaceAndPreAuthorizedCommands()
    {
        ChatSession session = InProcessSchedulerBackend.CreateRunSession(NewTask());

        Assert.Equal(Root, session.WorkspacePath);
        Assert.Equal(["git commit*"], session.PreAuthorizedShellPatterns);
    }

    //================= 无人值守的审批收口 =================

    /// <summary>
    /// 无人值守时冒出来的审批请求<b>当场</b>被拒，绝不挂着等一个不会到来的点击。
    /// 「当场」是可断言的：返回的 Task 立刻就是完成态；交互式实现返回的是一个等用户点的未完成 Task。
    /// </summary>
    [Fact]
    public void UnattendedApproval_IsDeniedWithoutWaiting()
    {
        ApprovalResolver resolver = InProcessSchedulerBackend.DenyUnauthorizedApprovals(NewTask());

        Task<IReadOnlyList<ChatMessage>> pending = resolver([ShellRequest(), EditRequest()]);

        Assert.True(pending.IsCompleted, "无人值守的审批必须当场有结论,不能挂着等用户");
        IReadOnlyList<ChatMessage> responses = pending.Result;
        Assert.Equal(2, responses.Count); //每一条请求都要有配对回应,漏一条历史就对不上
        foreach (ChatMessage message in responses)
        {
            ToolApprovalResponseContent response =
                Assert.IsType<ToolApprovalResponseContent>(Assert.Single(message.Contents));
            Assert.False(response.Approved);
        }
    }

    /// <summary>
    /// 追加轮次用尽后回应给空，<c>TurnDriver</c> 的运行循环据此收工
    /// （见 <c>TurnDriverTests.Approval_ResolverReturningNothing_EndsTheTurn</c>）——
    /// 否则一个反复请求同一授权的模型能让无头任务一直转下去。
    /// </summary>
    [Fact]
    public async Task UnattendedApproval_StopsAfterTheRoundLimit()
    {
        ApprovalResolver resolver = InProcessSchedulerBackend.DenyUnauthorizedApprovals(NewTask());

        for (int round = 1; round <= 4; round++)
        {
            Assert.NotEmpty(await resolver([ShellRequest()]));
        }

        Assert.Empty(await resolver([ShellRequest()])); //第五轮:收工
    }

    /// <summary>
    /// 被拒的动作必须在日志里留痕：无人值守只留下一个任务状态，
    /// 「为什么什么都没干」除了日志无处可查。要能看出是<b>谁</b>被拒、拒的是<b>什么</b>、<b>为什么</b>。
    /// </summary>
    [Fact]
    public async Task DeniedApproval_LeavesATraceInTheLog()
    {
        ScheduledAgentTask task = NewTask(EAgentPermissionMode.FullAuto);
        using LogCapture capture = new(text => text.Contains(task.TaskId));

        await InProcessSchedulerBackend.DenyUnauthorizedApprovals(task)([EditRequest("/etc/hosts")]);

        string line = Assert.Single(capture.Lines);
        Assert.Contains(task.DisplayName, line); //谁
        Assert.Contains("Edit[/etc/hosts]", line); //拒的是什么(工具 + 那个关键参数)
        Assert.Contains("FullAuto", line); //为什么:这一档不允许(越界写入贯穿三档)
    }

    /// <summary>轮次上限那一下也要留痕，否则任务"提前收工"看起来和跑完了一模一样</summary>
    [Fact]
    public async Task ApprovalRoundLimit_LeavesATraceInTheLog()
    {
        ScheduledAgentTask task = NewTask();
        ApprovalResolver resolver = InProcessSchedulerBackend.DenyUnauthorizedApprovals(task);
        using LogCapture capture = new(text => text.Contains("approval limit"));

        for (int round = 1; round <= 5; round++) await resolver([ShellRequest()]);

        Assert.Contains(task.TaskId, Assert.Single(capture.Lines));
    }

    /// <summary>
    /// 拒绝理由是送给模型看的，必须说清"无人值守"而不是"用户不同意"——
    /// 后者会让模型换个说法再请求一次，把四轮上限白白烧掉。
    /// </summary>
    [Fact]
    public async Task DenialReason_TellsTheModelNobodyIsThere()
    {
        IReadOnlyList<ChatMessage> responses =
            await InProcessSchedulerBackend.DenyUnauthorizedApprovals(NewTask())([ShellRequest()]);

        ToolApprovalResponseContent response = Assert.IsType<ToolApprovalResponseContent>(
            Assert.Single(Assert.Single(responses).Contents));
        Assert.Contains("Unattended run", response.Reason);
        Assert.Contains("Do not retry", response.Reason);
    }

    /// <summary>
    /// 抓一段警告日志：临时把自己装成 <see cref="LogManager"/> 的 <see cref="ILogger"/>，用完还原。
    ///
    /// 只能这么抓——<c>LogManager.LogWarning</c> 写的是 <c>Logger?.Warning(str, AddLog(str))</c>，
    /// 空条件调用会连实参一起短路，所以没装 logger 时 <c>OnLogChange</c> 压根不会响。
    /// 这是个全局开关，并行跑的其他测试的日志也会流过来，靠 <c>predicate</c> 滤掉。
    /// 不碰磁盘：落盘只发生在 <c>SaveLog</c>，这里不调它。
    /// </summary>
    private sealed class LogCapture : ILogger, IDisposable
    {
        private readonly Func<string, bool> _predicate;
        private readonly ILogger? _previous;

        public List<string> Lines { get; } = new();

        public LogCapture(Func<string, bool> predicate)
        {
            _predicate = predicate;
            _previous = LogManager.Instance.Logger;
            LogManager.Instance.Logger = this;
        }

        public void Dispose() => LogManager.Instance.Logger = _previous;

        public void Debug(string rawMessage, LogItem message)
        {
        }

        public void Warning(string rawMessage, LogItem message)
        {
            if (_predicate(rawMessage)) Lines.Add(rawMessage);
        }

        public void Error(string rawMessage, LogItem message)
        {
        }
    }
}
