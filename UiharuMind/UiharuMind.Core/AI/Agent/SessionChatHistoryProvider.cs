/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Runtime.Backends;
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

    /// <summary>框架给注入消息打的溯源标记键</summary>
    private const string AttributionKey = "_attribution";

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

        IEnumerable<ChatMessage> history = TrimToTokenBudget(session.History,
            ChatSettingConfig.Current.HistoryTokenBudget, CountTokensCached);
        return new ValueTask<IEnumerable<ChatMessage>>(history);
    }

    /// <summary>
    /// 按 token 预算裁剪历史——模型输入侧的开窗,UI 渲染与磁盘历史始终全量。
    /// 框架的在环压缩需要 MaxContextWindowTokens+MaxOutputTokens 才会构造,我们从未配置
    /// (等于不存在),模型侧也没有上下文长度元数据,因此预算走配置、裁剪在历史供给处统一做:
    /// 从最新往回装到预算为止;窗口起点不得落在工具调用组内部——孤儿工具结果会被模型 API 拒绝;
    /// 发生裁剪时窗口头部注入一条带 _attribution 标记的提示,它既不会被持久化也不会渲染为用户气泡。
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <param name="budgetTokens">token 预算;&lt;=0 表示不限</param>
    /// <param name="countTokens">单条消息的 token 估算器</param>
    /// <returns>预算内的历史窗口(始终为新列表)</returns>
    internal static List<ChatMessage> TrimToTokenBudget(IReadOnlyList<ChatMessage> history,
        int budgetTokens, Func<ChatMessage, int> countTokens)
    {
        if (budgetTokens <= 0 || history.Count == 0) return new List<ChatMessage>(history);

        int start = history.Count;
        long used = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            used += countTokens(history[i]);
            if (used > budgetTokens && start < history.Count) break;
            start = i;
            if (used > budgetTokens) break; //最新一条独自超预算:也只保它,当前轮次不能没有上文
        }

        // 起点若是孤儿工具结果,向更新方向跳过直到干净的组边界
        while (start < history.Count && IsToolResultMessage(history[start])) start++;

        if (start == 0) return new List<ChatMessage>(history);

        List<ChatMessage> window = new(history.Count - start + 1) { CreateTrimNotice(start) };
        for (int i = start; i < history.Count; i++) window.Add(history[i]);
        return window;
    }

    private static bool IsToolResultMessage(ChatMessage message)
    {
        return message.Role == ChatRole.Tool || message.Contents.Any(x => x is FunctionResultContent);
    }

    private static ChatMessage CreateTrimNotice(int omittedCount)
    {
        return new ChatMessage(ChatRole.User,
            $"[Context notice: {omittedCount} earlier messages were trimmed to fit the context budget. " +
            "The conversation continues below.]")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [AttributionKey] = "HistoryTrim" },
        };
    }

    private static readonly ConditionalWeakTable<ChatMessage, object> TokenCountCache = new(); //消息不可变,估算一次终身复用

    private static int CountTokensCached(ChatMessage message)
    {
        if (TokenCountCache.TryGetValue(message, out object? cached)) return (int)cached;
        int count = EstimateTokens(message);
        TokenCountCache.AddOrUpdate(message, count);
        return count;
    }

    private static int EstimateTokens(ChatMessage message)
    {
        int total = 8; //角色与消息结构的固定开销
        foreach (AIContent content in message.Contents)
        {
            total += content switch
            {
                TextContent text => LlmTokenizer.CountTokens(text.Text),
                TextReasoningContent reasoning => LlmTokenizer.CountTokens(reasoning.Text),
                FunctionCallContent call => 16 + LlmTokenizer.CountTokens(call.Name) +
                                            LlmTokenizer.CountTokens(call.Arguments == null
                                                ? string.Empty
                                                : string.Join(' ', call.Arguments.Values)),
                FunctionResultContent result => 16 + LlmTokenizer.CountTokens(result.Result?.ToString() ?? string.Empty),
                _ => 256, //图片等二进制内容按固定开销粗估
            };
        }

        return total;
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
        return message.AdditionalProperties?.ContainsKey(AttributionKey) != true;
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
