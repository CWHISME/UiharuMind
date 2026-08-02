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

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 基于 Microsoft.Agents.AI Harness 的 <see cref="ICharacterRunner"/> 实现。
/// 框架类型(AIAgent / AgentSession / TodoProvider 等)全部止步于此类内部。
/// </summary>
internal sealed class HarnessCharacterRunner : ICharacterRunner
{
    private AgentHandle? _handle;
    private AgentSession? _session;
    private string? _boundSessionId;
    private ChatSession? _attachedSession; //当前挂接的会话本体,供惰性客户端按请求解析会话级模型
    private string _handleFingerprint = string.Empty;

    public bool HasSession => _session != null;

    public async Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        _attachedSession = session;
        await EnsureHandleAsync(session, cancellationToken).ConfigureAwait(false);

        if (_boundSessionId == session.SessionId && _session != null) return;

        _session = await RestoreOrCreateSessionAsync(RequireHandle(), session.SessionId, cancellationToken)
            .ConfigureAwait(false);
        SessionChatHistoryProvider.Bind(_session, session.SessionId);
        _boundSessionId = session.SessionId;
    }

    /// <summary>
    /// 装配指纹变化时重建 agent。模型经惰性客户端按请求解析，切换模型无需重建；
    /// 影响装配的是角色、工作目录与权限档。
    /// </summary>
    private async Task EnsureHandleAsync(ChatSession session, CancellationToken cancellationToken)
    {
        string fingerprint = $"{session.CharacterId}|{session.WorkspacePath}|{session.PermissionModeIndex}";
        if (_handle != null && _handleFingerprint == fingerprint) return;

        AgentHandle newHandle = await AgentHost.Instance.CreateAgentAsync(new AgentBuildProfile
        {
            Character = session.CharacterData,
            WorkspacePath = session.WorkspacePath,
            PermissionMode = (EAgentPermissionMode)Math.Clamp(session.PermissionModeIndex, 0, 2),
            PromptArguments = session.CustomParams,
            // 按请求时的挂接会话取会话级模型(识图技能等会给临时会话绑定视觉模型),
            // 闭包读字段而非捕获参数:同一 handle 会跨会话复用
            SessionModelSource = () => _attachedSession?.ChatModelRunningData,
        }, cancellationToken).ConfigureAwait(false);

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
        _handleFingerprint = fingerprint;
    }

    public void ClearSession()
    {
        _session = null;
        _boundSessionId = null;
        _attachedSession = null;
    }

    public async Task SaveStateAsync()
    {
        if (_handle == null || _session == null || _boundSessionId == null) return;
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
        if (_handle == null || _session == null) yield break;

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
        if (_handle != null) await _handle.DisposeAsync().ConfigureAwait(false);
        _handle = null;
        _session = null;
        _boundSessionId = null;
    }

    private AgentHandle RequireHandle()
    {
        return _handle ?? throw new InvalidOperationException(
            $"{nameof(ICharacterRunner)} 尚未配置,请先调用 {nameof(AttachAsync)}。");
    }
}
