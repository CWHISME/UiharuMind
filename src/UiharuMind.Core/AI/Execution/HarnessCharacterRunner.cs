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
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Mcp;

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
    private ApprovalResolver? _turnApprovalResolver; //本轮的审批通道,子代理跑自己的轮次时共用
    private bool _turnAttended; //本轮有没有人看着,子代理的墙钟分档据此
    private readonly object _injectedGate = new();
    private readonly List<ChatMessage> _injected = new(); //已投入注入队列、尚未被模型消费的插话

    public bool HasSession => _session != null;

    public ChatOptions? ChatOptions => _handle?.ChatOptions;

    private ETurnBusy _busy;

    public ETurnBusy Busy => _busy;

    public Action? BusyChanged { get; set; }

    private void SetBusy(ETurnBusy value)
    {
        if (_busy == value) return;
        _busy = value;
        BusyChanged?.Invoke();
    }

    public void SetTurnContext(ApprovalResolver? resolver, bool isAttended)
    {
        _turnApprovalResolver = resolver;
        _turnAttended = isAttended;
    }

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
            // 闭包读字段:handle 跨轮次复用,而这两样每轮由 TurnDriver 交进来
            isAttendedSource: () => _turnAttended,
            subSessionStarted: (callId, subSessionId) => _activityChannel?.Writer
                .TryWrite(new SubSessionStartedContent(callId, subSessionId)));

        // MCP 工具是异步取回的,而快照与装配都从「此刻取得到什么」出发——所以等待必须在采集之前。
        // 放到这里而不是应用启动时:托管 server 的子进程因此只在真要用它的那一刻才起来,
        // 而这个应用还有截图、剪贴板、快捷问答一堆与 MCP 无关的功能(见 McpManager.WarmupAsync)
        if (profile.Character.IsAgent)
        {
            // 名单与下面 Resolve 用的是同一份:这一轮挂不上的 server 不值得为它起进程、也不值得等。
            // 忙碌态只在真的要等时才亮(回调由 WarmupAsync 决定发不发),否则每次装配都会闪一帧
            await McpManager.Instance.WarmupAsync(
                    profile.WorkspacePath,
                    profile.Character.Tools.DisabledMcpServers,
                    isWaiting => SetBusy(isWaiting ? ETurnBusy.ConnectingMcp : ETurnBusy.None),
                    cancellationToken)
                .ConfigureAwait(false);
        }

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
        // 普通角色禁用了 todo/mode/审批等全部有状态提供器,框架 blob 无内容可存;
        // 恢复路径找不到该文件时会新建框架会话并重新 Bind,行为不变
        if (_attachedSession?.CharacterData.IsAgent != true) return;

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
            ChatSession? attached = _attachedSession;

            // 消息边界由落盘那一刻给出:框架每完成一次服务调用就落一次盘。
            // 它只用来分消息,不用来定插话气泡的位置——实测落盘会晚于下一次调用的
            // 首条内容十几秒(上一次工具跑了十分钟那种),照它定位就把插话排到了回复中间
            Action onPersisted = () => channel.Writer.TryWrite(MessageBoundaryContent.Instance);
            if (attached != null) attached.ServiceCallPersisted += onPersisted;

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
                        // 每条 update 之前问一次"插话被取走了没":框架在每次服务调用发出去之前
                        // 排空注入队列(MessageInjectingChatClient.DrainInjectedMessagesAsync),
                        // 所以"已不在队列里"这一刻就是它被消费的那一刻,气泡因此排在
                        // 这次调用的任何产出之前。没有在途插话时是一次 Count 判断,不加锁不异步
                        await EmitConsumedInjectionsAsync(handle, session, channel, pumpSource.Token)
                            .ConfigureAwait(false);

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
                if (attached != null) attached.ServiceCallPersisted -= onPersisted;
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

    public AgentCapabilitySnapshot GetCapabilities()
    {
        AgentCapabilitySnapshot snapshot = _handle?.Capabilities ?? AgentCapabilitySnapshot.Empty;
        // 普通角色(纯提示词)的 handle 不登记任何提示词段(框架全关),
        // 但角色段是真实的固定开销——与空态预览同口径补上,聊天过程中能力统计不跳成空的
        if (_attachedSession?.CharacterData is { } character && !character.IsAgent)
        {
            return AgentCapabilitySnapshot.FromRoleplay(character);
        }

        return snapshot;
    }

    public TurnInputEstimate? InputEstimate => _handle?.InputEstimate;

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

        // 先登记再入队:入队之后队列随时可能被取走,登记晚了就认不出"它被消费了"
        List<ChatMessage> list = messages.ToList();
        lock (_injectedGate) _injected.AddRange(list);
        await _handle.MessageInjector.EnqueueMessagesAsync(_session, list).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task CancelInjectionsAsync(IReadOnlyCollection<ChatMessage> messages)
    {
        if (_handle?.MessageInjector == null || _session == null || messages.Count == 0) return;

        foreach (ChatMessage message in messages)
        {
            // 仍在队列里 = 未消费,可以安全撤回:从队列摘掉,并把登记表里那条也移除——
            // 登记表是"它会被发进内容流"的依据,留着会让界面认定已消费而多画一个气泡。
            // 已被消费(撤不回来)的静默放过,它此刻已经画进时间轴,
            // 界面提示由 UserMessageRendered 自己撤,这里不碰。
            bool removed = await PendingInjectionQueueAccess.RemoveAsync(
                _handle.MessageInjector, _session, message).ConfigureAwait(false);
            if (removed)
            {
                lock (_injectedGate) _injected.Remove(message);
            }
        }
    }

    /// <summary>
    /// 登记过的插话里凡是<b>已不在注入队列里</b>的，就是已被框架取走消费的，
    /// 按被消费的口径发进内容流——由调用方在写这条 update 的内容之前调用，
    /// 气泡因此排在消费它的那次调用的任何产出之前。
    ///
    /// 判据是队列快照而不是落盘边界：框架在每次服务调用发出去之前排空队列
    /// （<c>MessageInjectingChatClient.DrainInjectedMessagesAsync</c>），
    /// "已不在队列里"因此就是"被这次调用消费了"；而落盘边界只是<b>上一次</b>调用的收尾，
    /// 它可能晚于下一次调用的首条内容到达（上一次工具跑了十分钟时实测晚十几秒），
    /// 照它定位气泡就会插进回复中间。
    ///
    /// 没有在途插话时只是一次 <c>Count</c> 判断（不加锁、不异步），所以逐条 update 问它不贵。
    /// </summary>
    private async Task EmitConsumedInjectionsAsync(AgentHandle handle, AgentSession session,
        Channel<AIContent> channel, CancellationToken cancellationToken)
    {
        List<ChatMessage> tracked;
        lock (_injectedGate)
        {
            if (_injected.Count == 0) return;
            tracked = _injected.ToList();
        }

        IReadOnlyList<ChatMessage> pending = handle.MessageInjector == null
            ? []
            : await handle.MessageInjector.GetPendingMessagesAsync(session, cancellationToken).ConfigureAwait(false);

        foreach (ChatMessage message in tracked)
        {
            if (pending.Any(x => ReferenceEquals(x, message))) continue;

            lock (_injectedGate) _injected.Remove(message);
            channel.Writer.TryWrite(new UserMessageContent(message, isInterjection: true));
        }
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
