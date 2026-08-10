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
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

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
        if (session == null) return new ValueTask<IEnumerable<ChatMessage>>([]);

        // 文本侧全量供给,开窗交给框架的在环压缩(ADR 0006):它按当前模型的上下文动态定预算,
        // 先折叠老的工具结果、必要时才截断,组边界由 CompactionMessageIndex 保证不会产生孤儿工具结果。
        // 图片是唯一在这里先处理的:压缩按字节数估 token,老图片不清掉会让估算一路虚高
        IEnumerable<ChatMessage> history =
            HistoryImageWindow.DemoteOldImages(session.History, HistoryImageWindow.KeepRecentImages);
        return new ValueTask<IEnumerable<ChatMessage>>(history);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        ChatSession? session = Resolve(context.Session);
        if (session == null) return default;

        int before = session.History.Count;
        session.History.AddRange(context.RequestMessages.Where(IsOwnedByUs));
        if (context.ResponseMessages != null)
        {
            session.History.AddRange(context.ResponseMessages.Where(IsOwnedByUs));
        }

        // 常规轮次只追加新消息,落盘成本与会话长度无关
        if (session.History.Count != before) session.SaveAppended(before);
        return default;
    }

    // [MFA绕坑] 绕:框架注入消息混进待持久化列表 因:基类 ChatHistory 过滤挡不住 AIContextProvider 来源,per-service-call 路径下更是全漏 删除条件:框架把注入消息与真实对话分流
    /// <summary>
    /// 只有真正的用户输入与模型输出属于我们的历史。
    /// 框架注入的消息（回放用的历史副本、todo 快照、mode 切换通知、记忆片段等）
    /// 都带 _attribution 溯源标记：它们每轮由各 provider 重新生成，
    /// 一旦写进历史就会逐轮累积，并在下一轮又经历史回灌一次。
    /// 不能只依赖基类默认的 ChatHistory 过滤——它挡不住 AIContextProvider 来源，
    /// 而 per-service-call 持久化路径下连 ChatHistory 来源的也会漏进来。
    /// </summary>
    internal static bool IsOwnedByUs(ChatMessage message)
    {
        return message.AdditionalProperties?.ContainsKey(ChatMessageAnnotations.Attribution) != true;
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
