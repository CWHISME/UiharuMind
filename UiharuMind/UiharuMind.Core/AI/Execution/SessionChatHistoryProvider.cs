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

        // 有交接文档就从它开始供给:它之前的历史已经被压进那份文档,只留在会话文件与界面上。
        // 起点每次现算(扫最后一条交接消息)而不是记个下标——分支会话、删消息都不会把它算错
        IReadOnlyList<ChatMessage> full = session.History;
        int start = HistoryHandoff.SupplyStartIndex(full);
        IReadOnlyList<ChatMessage> supplied = start == 0
            ? full
            : full.Skip(start).ToList();

        // 其余开窗交给框架的在环压缩(ADR 0006):按当前模型的上下文动态定预算,先折叠老的工具结果、
        // 必要时才截断,组边界由 CompactionMessageIndex 保证不会产生孤儿工具结果。
        //
        // 图片曾经在这里再过一道 HistoryImageWindow.DemoteOldImages(只留最近两条带图消息)。
        // 现已停用,那道窗有两条理由,如今都不成立:
        //   1. 防「字节数/4」的虚高估算——已由 HistoryCompaction.CorrectedTokenCount 从源头修掉,
        //      而且那条路不改发出去的字节,零缓存代价;
        //   2. 省上传体积——已由 ConversationImageDownscaler 把单张压到 150KB 级。
        // 相反,它的代价很实:每来一张新图就改写一条靠前的历史,从那个位置往后的服务端前缀缓存
        // 全部失效,等于一次近乎全量的缓存丢失;被降级的图模型也彻底够不着了。
        //
        // 代码与测试保留。若实测发现长对话里图片累积仍然吃掉太多真实上下文(状态栏占用涨得离谱),
        // 接回去即可——届时更该按「图片总字节预算」触发,而不是现在这个按条数的窗口。
        return new ValueTask<IEnumerable<ChatMessage>>(supplied);
    }

    // [MFA绕坑] 绕:取消轮次时自己补写请求消息 因:基类 InvokedCoreAsync 把"有异常"一律当作本轮没发生过,直接跳过持久化 删除条件:框架区分取消与真失败
    /// <summary>
    /// 用户点停止在框架眼里是一次失败的调用，基类于是跳过持久化——刚发出去的那条用户消息
    /// 就此不进历史，界面上还留着，下一轮请求里却凭空少一条。取消不等于没发生，这里补上请求消息。
    /// </summary>
    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (context.InvokeException is OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            // 令牌此刻已被取消,存盘本身是同步的,传 None 免得被后续 API 当作又一次取消
            return StoreChatHistoryAsync(context, CancellationToken.None);
        }

        return base.InvokedCoreAsync(context, cancellationToken);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        ChatSession? session = Resolve(context.Session);
        if (session == null) return default;

        int before = session.History.Count;
        DateTimeOffset storedAt = DateTimeOffset.Now;
        // 请求消息属于本轮开始的那一刻,响应消息属于此刻:一轮可能跑几分钟,
        // 两者用同一个时间会让用户消息显示得比模型回复还晚
        AppendOwned(session, context.RequestMessages, session.TurnStartedAt ?? storedAt);
        if (context.ResponseMessages != null) AppendOwned(session, context.ResponseMessages, storedAt);
        session.TurnStartedAt = null; //一次性凭据,用过即弃

        // 常规轮次只追加新消息,落盘成本与会话长度无关
        if (session.History.Count != before) session.SaveAppended(before);
        return default;
    }

    /// <summary>
    /// 把属于我们的消息追加进历史，并给缺时间戳的补上回落值。
    ///
    /// 框架交给持久化的消息不带 <c>CreatedAt</c>——它连我们在
    /// <c>ChatSession.CreateMessage</c> 里给用户消息盖的那份也丢了（是重建的副本），
    /// 而 <c>ChatSession.LastTime</c> 与气泡上那行时间读的正是它。
    /// 就地写而不是克隆消息：克隆得连 <c>AIContent</c> 的多态与 <c>AdditionalProperties</c>
    /// 一起搬，而这个字段框架自己不参与判断，补上没有副作用。
    /// </summary>
    /// <param name="session">目标会话</param>
    /// <param name="messages">待追加的消息</param>
    /// <param name="fallback">缺时间戳时用的时间</param>
    internal static void AppendOwned(ChatSession session, IEnumerable<ChatMessage> messages,
        DateTimeOffset fallback)
    {
        foreach (ChatMessage message in messages)
        {
            if (!IsOwnedByUs(message)) continue;
            message.CreatedAt ??= fallback;
            session.History.Add(message);
        }
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
        // 交接文档是我们自己直接写进历史的,它没有 _attribution(那个键意味着不落盘,正好相反)。
        // 一旦它随供给的历史回到 RequestMessages 里,这里就会把它当成新消息逐轮重复追加,
        // 于是每轮多一份、下一轮又多一份。它永远不该从这条路进历史
        if (HistoryHandoff.IsNote(message)) return false;

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
