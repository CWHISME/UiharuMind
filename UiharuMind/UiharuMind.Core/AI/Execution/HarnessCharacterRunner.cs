/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.Memory;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 基于 Microsoft.Agents.AI Harness 的 <see cref="ICharacterRunner"/> 实现。
/// 框架类型(AIAgent / AgentSession / TodoProvider 等)全部止步于此类内部。
/// </summary>
internal sealed class HarnessCharacterRunner : ICharacterRunner
{
    // 同一会话的挂接/运行/存档/释放串行化:并发请求排队而非交错,
    // 避免流式进行中重建装配把使用中的 handle(含 shell executor)当场释放
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AgentHandle? _handle;
    private AgentSession? _session;
    private string? _boundSessionId;
    private ChatSession? _attachedSession; //当前挂接的会话本体,供惰性客户端按请求解析会话级模型
    private AgentAssemblyFacts? _lastSnapshot; //上次装配消费的输入快照
    private Channel<AIContent>? _activityChannel; //本轮的输出通道,委派型工具的过程经此并入内容流

    public bool HasSession => _session != null;

    public ChatOptions? ChatOptions => _handle?.ChatOptions;

    public async Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _attachedSession = session;
            await EnsureHandleAsync(session, cancellationToken).ConfigureAwait(false);

            if (_boundSessionId != session.SessionId || _session == null)
            {
                _session = await RestoreOrCreateSessionAsync(RequireHandle(), session.SessionId, cancellationToken)
                    .ConfigureAwait(false);
                SessionChatHistoryProvider.Bind(_session, session.SessionId);
                _boundSessionId = session.SessionId;
            }

            ApplyFileMemoryFolder(session, _session);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 装配快照变化时重建 agent。快照收录装配消费的全部输入(含重算好的系统提示词)，
    /// 角色卡编辑、会话参数、能力开关、MCP 工具集变化都被捕获；
    /// 模型经惰性客户端按请求解析，切换模型无需重建。
    /// </summary>
    private async Task EnsureHandleAsync(ChatSession session, CancellationToken cancellationToken)
    {
        // 每轮都造一份:它不过是几个字段与四个闭包,代价可忽略。
        // 而让快照与装配<b>从同一个 profile 出发</b>,正是"装配读了、快照没比"那类缺陷
        // 不再复发的前提——从前快照从 session 算、装配从 profile 算,是两条各自为政的路
        AgentBuildProfile profile = AgentBuildProfile.FromSession(session,
            // 按请求时的挂接会话取会话级模型/记忆库(识图技能等会给临时会话绑定视觉模型),
            // 闭包读字段而非捕获参数:同一 handle 会跨会话复用
            sessionModelSource: () => _attachedSession?.ChatModelRunningData,
            sessionKnowledgeSource: () => _attachedSession?.Memory,
            sessionShellApprovalSource: () => _attachedSession?.SnapshotSessionApprovedShellPatterns(),
            // 同样闭包读字段:handle 会跨轮次复用,而通道每轮新建
            activitySink: content => _activityChannel?.Writer.TryWrite(content));

        AgentAssemblyFacts snapshot = AgentAssemblyFacts.Capture(profile);
        if (_handle != null && snapshot.Equals(_lastSnapshot)) return;

        AgentHandle newHandle = CharacterRunnerFactory.Instance.CreateAgent(profile);

        if (_handle != null && _session != null)
        {
            // 附加状态按 provider 键存取,可跨 agent 实例迁移。
            // 历史不在其中,所以迁移失败最坏只丢 todos/mode。
            try
            {
                JsonElement serialized = await _handle.Agent
                    .SerializeSessionAsync(_session, cancellationToken: cancellationToken).ConfigureAwait(false);
                _session = await newHandle.Agent
                    .DeserializeSessionAsync(serialized, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warning($"Agent state migration failed, starting fresh: {e.Message}");
                _session = null;
                _boundSessionId = null;
            }
        }

        if (_handle != null) await _handle.DisposeAsync().ConfigureAwait(false);
        _handle = newHandle;
        _lastSnapshot = snapshot;
    }

    public async Task SaveStateAsync()
    {
        if (_handle == null || _session == null || _boundSessionId == null) return;
        // 角色扮演档禁用了 todo/mode/审批等全部有状态提供器,框架 blob 无内容可存;
        // 恢复路径找不到该文件时会新建框架会话并重新 Bind,行为不变
        if (_attachedSession?.CharacterData.Kind.IsAgent() != true) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            JsonElement state = await _handle.Agent.SerializeSessionAsync(_session).ConfigureAwait(false);
            await SessionManager.Instance.SaveAgentStateAsync(_boundSessionId, state).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // 附加状态可丢弃:历史已由 SessionChatHistoryProvider 写进会话本体
            Log.Warning($"Save agent state failed: {e.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 把文件记忆的工作目录钉成"该角色的那一个"。
    ///
    /// 必须每次挂接都做,不能只在新建会话时做:框架的默认 initializer 只对新会话生效,
    /// 而目录名进了会话状态包并随会话持久化——恢复回来的老会话里躺着的是旧值
    /// (框架默认给的 <c>{timestamp}_{guid}</c>,或改名前的目录名)。
    /// 覆写与 <see cref="FileMemoryLayout.Reconcile(CharacterData)"/> 的搬迁必须成对:
    /// 只搬不覆写,老会话仍指向旧名字,框架会照旧名重建一个空目录。
    /// </summary>
    private static void ApplyFileMemoryFolder(ChatSession session, AgentSession agentSession)
    {
        // 角色扮演档整个禁用了框架文件记忆,没有这份状态可写
        if (!session.CharacterData.Kind.IsAgent()) return;
        if (!session.CharacterData.Tools.EnableFileMemory) return;

        try
        {
            agentSession.StateBag.SetValue(FileMemoryLayout.StateKey,
                new FileMemoryState { WorkingFolder = FileMemoryLayout.Reconcile(session.CharacterData) });
        }
        catch (Exception e)
        {
            // 写不进去最坏是这轮沿用框架默认目录,不该让挂接失败
            Log.Warning($"Pin file memory folder failed: {e.Message}");
        }
    }

    private async Task<AgentSession> RestoreOrCreateSessionAsync(AgentHandle handle, string sessionId,
        CancellationToken cancellationToken)
    {
        using JsonDocument? state = await SessionManager.Instance.LoadAgentStateAsync(sessionId).ConfigureAwait(false);
        if (state != null)
        {
            try
            {
                return await handle.Agent
                    .DeserializeSessionAsync(state.RootElement, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warning($"Restore agent state '{sessionId}' failed, starting fresh: {e.Message}");
            }
        }

        return await handle.Agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 误用即炸:未挂接就运行是编程错误,静默无输出的症状("点了没反应")远比异常难查
            if (_handle == null || _session == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ICharacterRunner)} 尚未挂接会话,请先调用 {nameof(AttachAsync)}。");
            }

            // 框架流与委派型工具的过程合并成一条:两者都写进本轮通道,消费方只见一条有序流。
            // 必须让框架流也走通道——工具执行发生在框架迭代的内部,若在外层"框架流的间隙"里
            // 顺带排空通道,子代理的过程就只能等工具返回后才一次性冒出来,live 进度全丢。
            // 通道是本轮专属的:迟到的推送落进已弃用的通道被丢弃,不会串到下一轮。
            //
            // 无界是承重的,不要为了背压改成 bounded:委派型工具在框架迭代的内部执行,
            // 也就是在泵自己的执行流里往这个通道写——一旦写入会阻塞,泵就在等自己,当场死锁。
            Channel<AIContent> channel = Channel.CreateUnbounded<AIContent>();
            _activityChannel = channel;
            AgentHandle handle = _handle;
            AgentSession session = _session;
            // 消费方提前 break(不取消令牌)时用它给泵收尾,否则末尾的 await 会挂死
            CancellationTokenSource pumpSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (AgentResponseUpdate update in handle.Agent
                                       .RunStreamingAsync(messages, session, cancellationToken: pumpSource.Token)
                                       .ConfigureAwait(false))
                    {
                        foreach (AIContent content in update.Contents) channel.Writer.TryWrite(content);
                    }

                    channel.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    // 异常经通道交给消费方抛出;本任务自身永不抛,故末尾 await 它是安全的
                    channel.Writer.TryComplete(e);
                }
            }, CancellationToken.None);

            try
            {
                await foreach (AIContent content in channel.Reader.ReadAllAsync(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    yield return content;
                }
            }
            finally
            {
                _activityChannel = null;
                await pumpSource.CancelAsync().ConfigureAwait(false);
                await pump.ConfigureAwait(false);
                pumpSource.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<ChatMessage> GetHistory()
    {
        // 历史住在自有会话里,不再向框架索取
        if (_boundSessionId == null) return [];
        return SessionManager.Instance.Load(_boundSessionId)?.History ?? [];
    }

    public async Task<EAgentMode> GetModeAsync()
    {
        if (_handle?.Mode == null || _session == null) return EAgentMode.Execute;
        string mode = await _handle.Mode.GetModeAsync(_session).ConfigureAwait(false);
        return AgentModeExtensions.FromModeString(mode);
    }

    public async Task SetModeAsync(EAgentMode mode)
    {
        if (_handle?.Mode == null || _session == null) return;
        try
        {
            await _handle.Mode.SetModeAsync(_session, mode.ToModeString()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning($"Set agent mode failed: {e.Message}");
        }
    }

    public async Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync()
    {
        if (_handle?.Todos == null || _session == null) return [];

        IReadOnlyList<TodoItem> todos = await _handle.Todos.GetAllTodosAsync(_session).ConfigureAwait(false);
        List<TodoSnapshot> snapshots = new(todos.Count);
        foreach (TodoItem todo in todos)
        {
            snapshots.Add(new TodoSnapshot(todo.Title, todo.IsComplete));
        }

        return snapshots;
    }

    public async Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages)
    {
        if (_handle?.MessageInjector == null || _session == null) return false;
        await _handle.MessageInjector.EnqueueMessagesAsync(_session, messages).ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        // 与运行同闸:进行中的轮次结束后才释放 handle,不把使用中的 shell executor 抽走
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_handle != null) await _handle.DisposeAsync().ConfigureAwait(false);
            _handle = null;
            _session = null;
            _boundSessionId = null;
            _attachedSession = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private AgentHandle RequireHandle()
    {
        return _handle ?? throw new InvalidOperationException(
            $"{nameof(ICharacterRunner)} 尚未配置,请先调用 {nameof(AttachAsync)}。");
    }
}
