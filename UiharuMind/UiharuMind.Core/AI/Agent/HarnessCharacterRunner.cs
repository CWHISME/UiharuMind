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
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;

using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Agent;

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
    private AgentAssemblySnapshot? _lastSnapshot; //上次装配消费的输入快照

    public bool HasSession => _session != null;

    public async Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _attachedSession = session;
            await EnsureHandleAsync(session, cancellationToken).ConfigureAwait(false);

            if (_boundSessionId == session.SessionId && _session != null) return;

            _session = await RestoreOrCreateSessionAsync(RequireHandle(), session.SessionId, cancellationToken)
                .ConfigureAwait(false);
            SessionChatHistoryProvider.Bind(_session, session.SessionId);
            _boundSessionId = session.SessionId;
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
        AgentAssemblySnapshot snapshot = AgentAssemblySnapshot.Capture(session);
        if (_handle != null && snapshot.Equals(_lastSnapshot)) return;

        AgentHandle newHandle = AgentHost.Instance.CreateAgent(new AgentBuildProfile
        {
            Character = session.CharacterData,
            WorkspacePath = session.WorkspacePath,
            PermissionMode = (EAgentPermissionMode)Math.Clamp(session.PermissionModeIndex, 0, 2),
            PreAuthorizedShellPatterns = session.PreAuthorizedShellPatterns,
            PromptArguments = session.CustomParams,
            // 按请求时的挂接会话取会话级模型/记忆库(识图技能等会给临时会话绑定视觉模型),
            // 闭包读字段而非捕获参数:同一 handle 会跨会话复用
            SessionModelSource = () => _attachedSession?.ChatModelRunningData,
            SessionMemorySource = () => _attachedSession?.Memory,
        });

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
        if (_attachedSession?.CharacterData.Kind != ECharacterKind.Agent) return;

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

            await foreach (AgentResponseUpdate update in _handle.Agent
                               .RunStreamingAsync(messages, _session, cancellationToken: cancellationToken)
                               .ConfigureAwait(false))
            {
                foreach (AIContent content in update.Contents)
                {
                    yield return content;
                }
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

    public EAgentMode GetMode()
    {
        if (_handle?.Mode == null || _session == null) return EAgentMode.Execute;
        return AgentModeExtensions.FromModeString(_handle.Mode.GetMode(_session));
    }

    public void SetMode(EAgentMode mode)
    {
        if (_handle?.Mode == null || _session == null) return;
        try
        {
            _handle.Mode.SetMode(_session, mode.ToModeString());
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

    public bool TryInject(IEnumerable<ChatMessage> messages)
    {
        if (_handle?.MessageInjector == null || _session == null) return false;
        _handle.MessageInjector.EnqueueMessages(_session, messages);
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
