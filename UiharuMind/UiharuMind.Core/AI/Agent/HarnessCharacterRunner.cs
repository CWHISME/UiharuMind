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
    private string? _handleWorkspace;
    private EAgentPermissionMode? _handlePermissionMode;

    public bool HasSession => _session != null;

    public async Task ConfigureAsync(string? workspacePath, EAgentPermissionMode permissionMode,
        CancellationToken cancellationToken = default)
    {
        // 模型经惰性客户端按请求解析,切换模型无需重建;只有 workspace 与权限档影响装配
        bool needRebuild = _handle == null ||
                           _handleWorkspace != workspacePath ||
                           _handlePermissionMode != permissionMode;
        if (!needRebuild) return;

        AgentHandle newHandle = await AgentHost.Instance.CreateAgentAsync(new AgentBuildProfile
        {
            WorkspacePath = workspacePath,
            PermissionMode = permissionMode,
        }, cancellationToken).ConfigureAwait(false);

        if (_handle != null && _session != null)
        {
            // 会话状态按 provider 键存取,可跨 agent 实例迁移
            try
            {
                JsonElement serialized = await _handle.Agent
                    .SerializeSessionAsync(_session, cancellationToken: cancellationToken).ConfigureAwait(false);
                _session = await newHandle.Agent
                    .DeserializeSessionAsync(serialized, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warning($"Session migration failed, starting fresh session state: {e.Message}");
                _session = null;
            }
        }

        if (_handle != null) await _handle.DisposeAsync().ConfigureAwait(false);
        _handle = newHandle;
        _handleWorkspace = workspacePath;
        _handlePermissionMode = permissionMode;
    }

    public async Task EnsureSessionAsync(CancellationToken cancellationToken = default)
    {
        AgentHandle handle = RequireHandle();
        _session ??= await handle.Agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryLoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        AgentHandle handle = RequireHandle();
        _session = await AgentSessionIndex.Instance.LoadSessionAsync(handle.Agent, sessionId).ConfigureAwait(false);
        return _session != null;
    }

    public void ClearSession()
    {
        _session = null;
    }

    public async Task SaveSessionAsync(AgentSessionMeta meta)
    {
        if (_handle == null || _session == null) return;
        await AgentSessionIndex.Instance.SaveSessionAsync(_handle.Agent, _session, meta).ConfigureAwait(false);
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
        if (_handle == null || _session == null) return [];
        return _handle.History.GetMessages(_session);
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
    }

    private AgentHandle RequireHandle()
    {
        return _handle ?? throw new InvalidOperationException(
            $"{nameof(ICharacterRunner)} 尚未配置,请先调用 {nameof(ConfigureAsync)}。");
    }
}
