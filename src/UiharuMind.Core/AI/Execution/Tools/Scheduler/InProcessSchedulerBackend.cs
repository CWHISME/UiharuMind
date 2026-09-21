/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Scheduler;

/// <summary>
/// 进程内调度后端:任务列表 JSON 持久化,应用运行期内存计时器轮询触发;
/// 应用未运行不触发,启动时对过期任务按 MissedFirePolicy 处理(第一版:标记 Missed 等用户决定)。
/// 到点执行 = 建正式会话后经其唯一执行者(带任务自己那一档权限与 shell 预授权)无头跑一轮,
/// 规则之外的审批请求一律当场回拒并记一条警告日志——没人会来点那个按钮。
/// </summary>
public class InProcessSchedulerBackend : ISchedulerBackend, IDisposable
{
    private const int MaxApprovalRounds = 4; //拒绝造成的追加轮次上限,防御模型反复请求同一授权

    /// <summary>拒绝理由,会送给模型——说清是"无人值守"而不是"用户不同意",模型才不会反复请求</summary>
    private const string DenialReason =
        "Unattended run: this action is not allowed by the task's permission mode and was not " +
        "pre-authorized, so it was denied automatically. Do not retry it; work around it or stop.";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly List<ScheduledAgentTask> _tasks = new();
    private readonly object _locker = new();
    private readonly CancellationTokenSource _loopCancellation = new();

    public string BackendId => "in-process";

    public event Action<ScheduledAgentTask>? OnTaskUpdated;

    public InProcessSchedulerBackend()
    {
        List<ScheduledAgentTask>? loaded = SaveUtility.Load<List<ScheduledAgentTask>>(AppPaths.Data.ScheduledAgentTasks);
        if (loaded != null) _tasks.AddRange(loaded);

        // 上次运行遗留的 Running 状态视为失败;过期的待执行任务转 Missed
        foreach (ScheduledAgentTask task in _tasks)
        {
            if (task.Status == EScheduledTaskStatus.Running) task.Status = EScheduledTaskStatus.Failed;
            if (task.Status == EScheduledTaskStatus.Pending && task.FireAt <= DateTimeOffset.Now)
            {
                task.Status = task.MissedFirePolicy switch
                {
                    EMissedFirePolicy.Skip => EScheduledTaskStatus.Cancelled,
                    _ => EScheduledTaskStatus.Missed,
                };
            }
        }

        Save();
        _ = RunLoopAsync(_loopCancellation.Token);
    }

    public Task ScheduleAsync(ScheduledAgentTask task)
    {
        lock (_locker)
        {
            _tasks.Add(task);
        }

        Save();
        NotifyUpdated(task);
        return Task.CompletedTask;
    }

    public Task CancelAsync(string taskId)
    {
        ScheduledAgentTask? task = FindTask(taskId);
        if (task is { Status: EScheduledTaskStatus.Pending or EScheduledTaskStatus.Missed })
        {
            task.Status = EScheduledTaskStatus.Cancelled;
            Save();
            NotifyUpdated(task);
        }

        return Task.CompletedTask;
    }

    public async Task RunNowAsync(string taskId)
    {
        ScheduledAgentTask? task = FindTask(taskId);
        if (task is
            {
                Status: EScheduledTaskStatus.Pending or EScheduledTaskStatus.Missed or EScheduledTaskStatus.Failed
            })
        {
            await ExecuteAsync(task).ConfigureAwait(false);
        }
    }

    public Task SetPermissionModeAsync(string taskId, EAgentPermissionMode mode)
    {
        ScheduledAgentTask? task = FindTask(taskId);
        // 只对还没跑的任务开放:跑过的任务改档位只会让列表上的显示与实际执行过的那一档不符
        if (task is { Status: EScheduledTaskStatus.Pending or EScheduledTaskStatus.Missed } &&
            task.PermissionMode != mode)
        {
            task.PermissionMode = mode;
            Save();
            Log.Debug($"Scheduled agent task '{task.DisplayName}' permission mode set to {mode}.");
            NotifyUpdated(task);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<ScheduledAgentTask> GetTasks()
    {
        lock (_locker)
        {
            return _tasks.OrderByDescending(x => x.FireAt).ToList();
        }
    }

    public void Dispose()
    {
        _loopCancellation.Cancel();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

                List<ScheduledAgentTask> due;
                lock (_locker)
                {
                    due = _tasks.Where(x => x.Status == EScheduledTaskStatus.Pending &&
                                            x.FireAt <= DateTimeOffset.Now).ToList();
                }

                foreach (ScheduledAgentTask task in due)
                {
                    await ExecuteAsync(task).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ExecuteAsync(ScheduledAgentTask task)
    {
        task.Status = EScheduledTaskStatus.Running;
        Save();
        NotifyUpdated(task);
        // 档位记进日志:事后回看"这一跑为什么写成了/没写成"第一个要问的就是它
        Log.Debug($"Scheduled agent task '{task.DisplayName}' started ({task.PermissionMode}).");

        // 无人值守跑出来的结果也是一个正式会话,与手动对话同一套存储。
        // 执行必须经会话的唯一执行者——运行中用户从列表点开该会话时,
        // 界面拿到的是同一个执行者,请求排队而不是出现第二条执行链交错写历史。
        ChatSession? chatSession = null;
        try
        {
            chatSession = CreateRunSession(task);
            SessionManager.Instance.Add(chatSession);
            task.ResultSessionId = chatSession.SessionId;

            NoteUnapprovedMcpServers(task, chatSession);

            await chatSession.Runner.AttachAsync(chatSession).ConfigureAwait(false);

            // 与界面跑的是同一份编排:运行态登记(这个会话就在会话列表里,⏰ 前缀,用户看得见它在跑,
            // 也因此不会在跑的过程中被删除或清空历史)、取消收尾、交接文档一并到手。
            // 差异只有两处——没有渲染落点(sink 为 null),审批一律拒绝
            bool failed = false;
            using TurnDriver driver = new(null, new TurnUsageLedger(),
                notice =>
                {
                    if (notice.Kind == ETurnNotice.Failed) failed = true;
                });
            await driver.RunAsync(chatSession, chatSession.Runner,
                    new ChatMessage(ChatRole.User, task.Prompt), DenyUnauthorizedApprovals(task))
                .ConfigureAwait(false);

            task.Status = failed ? EScheduledTaskStatus.Failed : EScheduledTaskStatus.Completed;
        }
        catch (Exception e)
        {
            Log.Error($"Scheduled agent task '{task.DisplayName}' failed: {e}");
            task.Status = EScheduledTaskStatus.Failed;
        }
        finally
        {
            // 任务结束就释放执行者(含 shell executor),不让它挂到应用退出;
            // 之后用户打开该会话会重新惰性创建
            if (chatSession != null) await chatSession.DisposeRunnerAsync().ConfigureAwait(false);
        }

        Save();
        NotifyUpdated(task);
    }

    /// <summary>
    /// 撞上未获授权的项目级 MCP server 时留一条痕，<b>然后照常跑</b>。
    ///
    /// 三种处置里选的是这一种：整个任务判失败，会让一个可有可无的 server 让定时任务停摆，
    /// 而 <c>.mcp.json</c> 是仓库作者改的、不一定是你；挂起等确认更不行——
    /// 与 <see cref="DenyUnauthorizedApprovals"/> 同一条口径，没人会来点那个按钮。
    /// 状态照旧算 <see cref="EScheduledTaskStatus.Completed"/>：它确实跑完了，区别写在会话里。
    ///
    /// <b>痕迹落进结果会话而不只是日志</b>：用户回看这一跑的落点是那个会话，不是日志文件。
    ///
    /// 正常情况下这里什么都不会发生——用户创建任务时选定的工作区，在那一刻就已经确认过了。
    /// 会走到这里的只有一种：创建之后、触发之前 <c>.mcp.json</c> 被改过（比如 <c>git pull</c>
    /// 拉进来一条新的），而那正是指纹要拦的东西。
    /// </summary>
    /// <param name="task">本次执行的任务</param>
    /// <param name="session">这一跑的结果会话</param>
    private static void NoteUnapprovedMcpServers(ScheduledAgentTask task, ChatSession session)
    {
        List<McpApprovalRequest> pending = McpManager.Instance.GetPendingApprovals(task.WorkspacePath);
        if (pending.Count == 0) return;

        string names = string.Join("、", pending.Select(x => x.Name));
        Log.Warning($"Scheduled agent task '{task.DisplayName}' ({task.TaskId}) ran without " +
                    $"unapproved workspace MCP servers: {names}");
        session.AddMessage(ChatRole.System,
            $"⚠️ 本次无人值守执行跳过了 {pending.Count} 个未确认的项目级 MCP server（{names}）。\n" +
            "它们来自工作区的 .mcp.json，需要你在场确认一次要执行的命令才会启用。" +
            "这一轮照常跑完，但模型没有这些工具。");
    }

    /// <summary>
    /// 造这一次无头运行用的会话。抽出来是为了让"档位取自任务字段"这件事可测——
    /// <see cref="ExecuteAsync"/> 本身要挂执行者、发请求，测不了。
    /// </summary>
    /// <param name="task">任务</param>
    /// <returns>尚未登记进 <c>SessionManager</c> 的会话</returns>
    internal static ChatSession CreateRunSession(ScheduledAgentTask task)
    {
        // 存档里出现枚举之外的数值(文件被手改坏、别的版本写过)时回落 AutoEdit。
        // 不能就这么传下去:下游 AgentBuildProfile 用的是 Clamp(0, 2),那会把一个越界的大数
        // 悄悄变成 FullAuto——坏数据不该是提权的路子
        EAgentPermissionMode mode = Enum.IsDefined(task.PermissionMode)
            ? task.PermissionMode
            : EAgentPermissionMode.AutoEdit;

        return new ChatSession
        {
            CharacterId = nameof(DefaultCharacter.WorkspaceAgent),
            Title = $"⏰ {task.DisplayName}",
            Description = task.Prompt,
            WorkspacePath = task.WorkspacePath,
            PermissionModeIndex = (int)mode,
            PreAuthorizedShellPatterns = task.PreAuthorizedCommands,
        };
    }

    /// <summary>
    /// 无头执行的审批策略：冒到这里的都是权限档与预授权之外的请求，一律<b>当场拒绝</b>
    /// （绝不静默挂起等待——没人会来点那个按钮）。追加轮次用尽后返回空，运行循环据此收工。
    ///
    /// 注意「一律拒绝」只对<b>冒出来的</b>请求成立：命中权限档或
    /// <see cref="ScheduledAgentTask.PreAuthorizedCommands"/> 的调用由装配层的
    /// 自动放行规则处理，根本不会走到这里。
    ///
    /// 每一次拒绝都记一条日志。这是无人值守唯一的现场：任务跑完只留下一个状态，
    /// 「为什么什么都没干」除了日志无处可查。三档各自会在这里被拒到什么：
    /// <see cref="EAgentPermissionMode.ReadOnly"/> 连工作区内的写入都拒、
    /// <see cref="EAgentPermissionMode.AutoEdit"/> 拒 shell 与越界写入、
    /// <see cref="EAgentPermissionMode.FullAuto"/> 只拒越界写入（ADR 0010 的硬规则，
    /// 无人值守时它就是工作区外唯一的拦阻）。
    /// </summary>
    /// <param name="task">本次执行的任务，日志要指名道姓说是谁被拒</param>
    /// <returns>一个带轮次计数的审批策略，一次执行用一个</returns>
    internal static ApprovalResolver DenyUnauthorizedApprovals(ScheduledAgentTask task)
    {
        int round = 0;
        return requests =>
        {
            if (++round > MaxApprovalRounds)
            {
                Log.Warning($"Scheduled agent task '{task.DisplayName}' ({task.TaskId}) hit the " +
                            $"{MaxApprovalRounds}-round approval limit; ending the run.");
                return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
            }

            IReadOnlyList<ChatMessage> denials = requests
                .Select(request =>
                {
                    Log.Warning($"Scheduled agent task '{task.DisplayName}' ({task.TaskId}) denied " +
                                $"{DescribeCall(request.ToolCall)}: not allowed under {task.PermissionMode} " +
                                "and not pre-authorized (unattended run, nobody to ask).");
                    return new ChatMessage(ChatRole.User,
                        new List<AIContent>
                            { request.CreateResponse(approved: false, reason: DenialReason) });
                })
                .ToList();
            return Task.FromResult(denials);
        };
    }

    /// <summary>把被拒的调用写成一行人能看懂的字(工具名 + 最关键的那个参数)</summary>
    private static string DescribeCall(AIContent? toolCall)
    {
        if (toolCall is not FunctionCallContent call) return toolCall?.ToString() ?? "an unknown tool call";

        string? detail = ApprovalModeMapper.ExtractCommand(call.Arguments) ??
                         ApprovalModeMapper.ExtractFilePath(call.Arguments);
        return string.IsNullOrWhiteSpace(detail) ? call.Name : $"{call.Name}[{detail}]";
    }

    private ScheduledAgentTask? FindTask(string taskId)
    {
        lock (_locker)
        {
            return _tasks.FirstOrDefault(x => x.TaskId == taskId);
        }
    }

    private void Save()
    {
        lock (_locker)
        {
            SaveUtility.Save(AppPaths.Data.ScheduledAgentTasks, _tasks);
        }
    }

    private void NotifyUpdated(ScheduledAgentTask task)
    {
        try
        {
            OnTaskUpdated?.Invoke(task);
        }
        catch (Exception e)
        {
            Log.Warning($"Scheduler update handler failed: {e.Message}");
        }
    }
}
