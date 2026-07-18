/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using AgentSessionMeta = UiharuMind.Core.AI.Agent.AgentSessionMeta;

namespace UiharuMind.Core.AI.Agent.Scheduler;

/// <summary>
/// 进程内调度后端:任务列表 JSON 持久化,应用运行期内存计时器轮询触发;
/// 应用未运行不触发,启动时对过期任务按 MissedFirePolicy 处理(第一版:标记 Missed 等用户决定)。
/// 到点执行 = 构建带预授权规则的 HarnessAgent 无头跑一轮,规则之外的审批请求一律回拒。
/// </summary>
public class InProcessSchedulerBackend : ISchedulerBackend, IDisposable
{
    private const string SaveFileName = "ScheduledAgentTasks.json";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly List<ScheduledAgentTask> _tasks = new();
    private readonly object _locker = new();
    private readonly CancellationTokenSource _loopCancellation = new();

    public string BackendId => "in-process";

    public event Action<ScheduledAgentTask>? OnTaskUpdated;

    public InProcessSchedulerBackend()
    {
        List<ScheduledAgentTask>? loaded = SaveUtility.LoadRootFile<List<ScheduledAgentTask>>(SaveFileName);
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
        Log.Debug($"Scheduled agent task '{task.DisplayName}' started.");

        AgentHandle? handle = null;
        try
        {
            handle = await AgentHost.Instance.CreateAgentAsync(new AgentBuildProfile
            {
                WorkspacePath = task.WorkspacePath,
                PermissionMode = EAgentPermissionMode.AutoEdit,
                PreAuthorizedShellPatterns = task.PreAuthorizedCommands,
            }).ConfigureAwait(false);

            AgentSession session = await handle.Agent.CreateSessionAsync().ConfigureAwait(false);
            AgentSessionMeta meta = AgentSessionIndex.Instance.CreateMeta(
                $"⏰ {task.DisplayName}", task.WorkspacePath, (int)EAgentPermissionMode.AutoEdit);
            task.ResultSessionId = meta.SessionId;

            bool succeeded = await RunHeadlessAsync(handle.Agent, session, task.Prompt).ConfigureAwait(false);
            await AgentSessionIndex.Instance.SaveSessionAsync(handle.Agent, session, meta).ConfigureAwait(false);

            task.Status = succeeded ? EScheduledTaskStatus.Completed : EScheduledTaskStatus.Failed;
        }
        catch (Exception e)
        {
            Log.Error($"Scheduled agent task '{task.DisplayName}' failed: {e}");
            task.Status = EScheduledTaskStatus.Failed;
        }
        finally
        {
            if (handle != null) await handle.DisposeAsync().ConfigureAwait(false);
        }

        Save();
        NotifyUpdated(task);
    }

    /// <summary>
    /// 无头执行一轮:预授权规则之外的审批请求一律拒绝(绝不静默挂起等待)
    /// </summary>
    private static async Task<bool> RunHeadlessAsync(AIAgent agent, AgentSession session, string prompt)
    {
        List<ChatMessage> nextMessages = new() { new ChatMessage(ChatRole.User, prompt) };
        // 拒绝造成的追加轮次设上限,防御模型反复请求同一授权
        for (int round = 0; round < 5 && nextMessages.Count > 0; round++)
        {
            List<ToolApprovalRequestContent> approvalRequests = new();
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(nextMessages, session)
                               .ConfigureAwait(false))
            {
                approvalRequests.AddRange(update.Contents.OfType<ToolApprovalRequestContent>());
            }

            nextMessages = approvalRequests
                .Select(request => new ChatMessage(ChatRole.User,
                    new List<AIContent>
                        { request.CreateResponse(approved: false, reason: "Unattended run: not pre-authorized.") }))
                .ToList();
        }

        return true;
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
            SaveUtility.SaveRootFile(SaveFileName, _tasks);
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
