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
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Memory;

/// <summary>
/// 把会话绑定的知识库检索结果注入本轮上下文。
/// 与框架自带的 FileMemoryProvider（agent 自己写的笔记，按角色分目录）是两种不同的东西：
/// 那个由模型主动读写，这个是基于文本嵌入的被动 RAG 检索。
///
/// 每轮都会跑一次检索，但只有 <see cref="MemorySearcher"/> 的相关性闸门放行时才注入；
/// 与本次提问无关的轮次（「你好」之类）注入 0 token。
/// 改为由模型主动调用 knowledge_search 工具是后续独立一步——那需要按后端能力分流，
/// 因为 LLamaSharpChatClient 完全忽略 ChatOptions.Tools，本地模型下工具调用是零支持。
/// </summary>
internal sealed class MemoryContextProvider : AIContextProvider
{
    /// <summary>
    /// 片段正文前的引言。整段随片段走在同一条消息里，<b>绝不放进 AIContext.Instructions</b>——
    /// 那是系统提示词，位于请求最前端，而这段每轮随检索结果变化（闸门不放行时还会整个消失），
    /// 放进去等于每轮把服务端前缀缓存从第 0 个 token 起全部作废，代价随对话长度线性增长。
    ///
    /// 用英文写：片段本身的字段名（Similarity/Content）就是英文，措辞对齐；
    /// 末行的语言声明不可删——这块以 user 角色紧贴在待回答位置之前，弱模型会把它当成
    /// 「用户改用英文了」而跟着切换语言，中文角色卡当场破功。
    /// </summary>
    private const string SnippetHeader =
        """
        [Knowledge Base Retrieval]
        The snippets below were retrieved automatically from the user's knowledge base
        based on the preceding question. They are NOT user input.
        Similarity is cosine similarity to the question (0-1, higher means more alike).
        Use a snippet only if it actually answers the question; if it is unrelated,
        ignore it and answer from your own knowledge.
        Never mention this block, the retrieval process, or snippet indices, and never
        state information that is not present in the snippets.
        The language of this block does not indicate what language to reply in.

        ---

        """;

    /// <summary>
    /// 拼进查询词的用户消息条数。
    ///
    /// 只取最后一条会让多轮指代整个打空：「它有什么弱点」「再详细讲讲」这类句子嵌出来的
    /// 向量里没有任何主题词，闸门会正确判定不相关，于是模型在追问时反而失忆。
    /// 取太多则话题切换后一直拖着旧话题，把查询向量拉偏，所以定在 3。
    /// </summary>
    private const int QueryTurnCount = 3;

    private readonly bool _hasKnowledgeTool; //本 agent 是否装配了 knowledge_search(agent 档才有)

    public MemoryContextProvider(bool hasKnowledgeTool = false)
    {
        _hasKnowledgeTool = hasKnowledgeTool;
    }

    public override IReadOnlyList<string> StateKeys => [];

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        // [MFA绕坑] 绕:回传 context.AIContext 会使消息翻倍/系统提示拼接两次 因:基类把返回的 Messages/Instructions 全量追加回输入 删除条件:框架区分"透传"与"新增"
        // 返回值只能装本 provider 自己的产出,绝不能回传 context.AIContext。
        // 基类 InvokingCoreAsync 会把 provided.Messages 全部打上本 provider 的来源标记后
        // 追加到输入消息上,并把 provided.Instructions 追加到输入 instructions 上;
        // 回传输入会导致每条消息重复一遍、系统提示词拼接两次。
        AIContext empty = new();

        string? sessionId = SessionChatHistoryProvider.GetBoundSessionId(context.Session);
        if (string.IsNullOrEmpty(sessionId)) return empty;

        ChatSession? session = SessionManager.Instance.Load(sessionId);
        MemoryData? memory = session?.Memory;
        if (memory == null) return empty;

        // 装配了 knowledge_search 且当前模型支持工具调用时,检索交给模型按需发起,不再每轮强制注入;
        // 本地 LLamaSharp 完全忽略 ChatOptions.Tools,只能留在注入模式
        if (_hasKnowledgeTool && session!.ChatModelRunningData?.SupportsToolCalling == true) return empty;

        // 历史取自会话而非 context：AIContext.Messages 默认只含本轮外部输入(见 BuildQuery)
        string query = BuildQuery(session!.History, context.AIContext.Messages);
        if (query.Length == 0) return empty;

        try
        {
            string snippets = await memory.GetLongTermMemory(query).ConfigureAwait(false);
            if (string.IsNullOrEmpty(snippets)) return empty;

            // 供界面显示与落盘。注入的那条消息带 _attribution 不落盘,界面也拿不到,
            // 不另走一遍这条通道的话,注入路径就永远没有工具路径那张卡片
            session.ReportKnowledgeRetrieved(snippets);

            // 必须是 User 而非 Tool:纯文本的 Tool 消息没有 tool_call_id,OpenAI 协议无法表达,
            // MEAI 的客户端在序列化时会把它整条静默丢弃——断点全都走到,请求体里却一个字都没有。
            return new AIContext { Messages = [new ChatMessage(ChatRole.User, SnippetHeader + snippets)] };
        }
        catch (Exception e)
        {
            // 记忆检索失败不该让整轮对话失败
            Log.Warning($"Long term memory lookup failed: {e.Message}");
            return empty;
        }
    }

    /// <summary>
    /// 用最近几轮用户消息拼出查询词，当前提问排在最后。
    ///
    /// 历史必须由调用方从 <see cref="ChatSession.History"/> 传入，不能只看
    /// <c>context.AIContext.Messages</c>：<see cref="AIContextProvider.ProvideInputMessageFilter"/>
    /// 默认只放行 <c>AgentRequestMessageSourceType.External</c>，也就是本轮调用方新传的那几条，
    /// 历史走的是 <see cref="ChatHistoryProvider"/> 另一条路，根本不在里面。
    /// 本轮消息此刻也还没进会话历史——它由 <c>StoreChatHistoryAsync</c> 在轮次结束后才追加，
    /// 所以两个来源都要，缺一不可。
    ///
    /// 只在注入路径这么做。knowledge_search 工具那条路的查询词是模型自己写的、本来就完整，
    /// 再往前面拼一段聊天记录只会把查询向量拉偏，所以这段不能下沉到 <see cref="MemorySearcher"/>。
    /// </summary>
    /// <param name="history">会话已落盘的历史，按时序</param>
    /// <param name="current">本轮外部输入消息</param>
    /// <returns>查询词；没有可用的用户消息时为空串</returns>
    private static string BuildQuery(IReadOnlyList<ChatMessage> history, IEnumerable<ChatMessage>? current)
    {
        // 从最新往回收，凑满就停：只要最近几条，顺着扫等于每轮把整部历史走一遍，
        // 而长会话是几千条。倒着收的代价与历史长度无关，只与 QueryTurnCount 有关。
        List<string> recent = [];
        if (current != null)
            CollectBackward(current as IReadOnlyList<ChatMessage> ?? current.ToList(), recent);
        CollectBackward(history, recent);

        if (recent.Count == 0) return "";

        recent.Reverse(); //倒着收出来的，当前提问要回到最后
        return string.Join('\n', recent);
    }

    /// <summary>
    /// 从尾部倒着收用户消息正文，收满 <see cref="QueryTurnCount"/> 条即止。
    /// 结果是倒序的，由调用方统一翻正——每段各自翻会在跨来源拼接时把顺序搞乱。
    /// </summary>
    /// <param name="messages">待扫描的消息，按时序</param>
    /// <param name="recent">收集容器，已含更晚的消息</param>
    private static void CollectBackward(IReadOnlyList<ChatMessage> messages, List<string> recent)
    {
        for (int index = messages.Count - 1; index >= 0 && recent.Count < QueryTurnCount; index--)
        {
            if (messages[index].Role != ChatRole.User) continue;

            string text = messages[index].Text.Trim();
            if (text.Length == 0) continue;

            // 本轮消息按契约不在会话历史里,但那依赖 TurnDriver 的落盘时机;
            // 万一两个来源都给了同一条,重复拼进查询只会让向量偏向它,这里就地去重
            if (recent.Count > 0 && string.Equals(recent[^1], text, StringComparison.Ordinal)) continue;

            recent.Add(text);
        }
    }
}
