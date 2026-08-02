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
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 以项目自有的 <see cref="ChatSession"/> 作为框架历史的后备存储。
///
/// 由此历史的权威来源变成我们的会话文件，框架序列化出的 blob 里只剩
/// todos / mode / 审批状态与一个会话标识指针，成为可丢弃的附属状态：
/// 它损坏或丢失只影响侧栏，不会像原先那样让整个会话打不开；
/// 重建 agent 实例(切换 workspace 或权限档)时也不再有丢失历史的风险。
///
/// 基类要求 provider 不得把会话相关状态放进自己的实例字段(同一个 provider 服务多个会话)，
/// 因此这里只往 <see cref="AgentSession.StateBag"/> 里存会话标识。
/// </summary>
internal sealed class SessionChatHistoryProvider : ChatHistoryProvider
{
    private const string SessionIdKey = "UiharuSessionId";

    /// <summary>
    /// 把框架会话与项目会话绑定。必须在首次运行前调用。
    /// </summary>
    /// <param name="agentSession">框架会话</param>
    /// <param name="sessionId">项目会话标识</param>
    public static void Bind(AgentSession agentSession, string sessionId)
    {
        agentSession.StateBag.SetValue(SessionIdKey, sessionId);
    }

    /// <summary>
    /// 取出框架会话所绑定的项目会话标识
    /// </summary>
    /// <param name="agentSession">框架会话</param>
    /// <returns>会话标识；未绑定为 null</returns>
    public static string? GetBoundSessionId(AgentSession? agentSession)
    {
        if (agentSession == null) return null;
        try
        {
            return agentSession.StateBag.TryGetValue(SessionIdKey, out string? sessionId) ? sessionId : null;
        }
        catch (Exception e)
        {
            Log.Warning($"Read bound session id failed: {e.Message}");
            return null;
        }
    }

    public override IReadOnlyList<string> StateKeys => [SessionIdKey];

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        ChatSession? session = Resolve(context.Session);
        IEnumerable<ChatMessage> history = session == null ? [] : session.History.ToList();
        return new ValueTask<IEnumerable<ChatMessage>>(history);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        ChatSession? session = Resolve(context.Session);
        if (session == null) return default;

        // 基类默认已把标记为 ChatHistory 来源的消息滤掉,这里收到的都是本轮新产生的
        session.History.AddRange(context.RequestMessages);
        if (context.ResponseMessages != null) session.History.AddRange(context.ResponseMessages);
        session.Save();
        return default;
    }

    private static ChatSession? Resolve(AgentSession? agentSession)
    {
        string? sessionId = GetBoundSessionId(agentSession);
        if (string.IsNullOrEmpty(sessionId)) return null;

        ChatSession? session = SessionManager.Instance.Load(sessionId);
        if (session == null) Log.Warning($"Bound chat session '{sessionId}' not found.");
        return session;
    }
}
