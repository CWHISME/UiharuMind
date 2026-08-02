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
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 把会话绑定的记忆库检索结果注入本轮上下文。
/// 与框架自带的 FileMemoryProvider 是互补而非重复：那个是模型主动读写记忆文件的工具式记忆，
/// 这个是基于文本嵌入的被动 RAG 检索。
///
/// 本阶段行为与旧实现一致（每轮拿最后一条用户消息去检索）。
/// 已知问题：不管需不需要都会跑一次嵌入，且用用户原话作查询词的召回质量差。
/// 改为由模型主动调用 memory_search 工具是后续独立一步——那需要按后端能力分流，
/// 因为 LLamaSharpChatClient 完全忽略 ChatOptions.Tools，本地模型下工具调用是零支持。
/// </summary>
internal sealed class MemoryContextProvider : AIContextProvider
{
    private const string InstructionsHeader =
        "以下是通过文本嵌入模型搜索到的相关信息片段，用户当前的问题极有可能与之相关，" +
        "请根据片段的相关性(Relevance)参数高低酌情参考。";

    public override IReadOnlyList<string> StateKeys => [];

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
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

        // 传入的 AIContext.Messages 已含调用方消息与历史消息(框架契约),取最后一条用户输入作查询
        string? query = context.AIContext.Messages?
            .LastOrDefault(x => x.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(query)) return empty;

        try
        {
            string snippets = await memory.GetLongTermMemory(query).ConfigureAwait(false);
            if (string.IsNullOrEmpty(snippets)) return empty;

            return new AIContext
            {
                Instructions = InstructionsHeader,
                Messages = [new ChatMessage(ChatRole.Tool, snippets)],
            };
        }
        catch (Exception e)
        {
            // 记忆检索失败不该让整轮对话失败
            Log.Warning($"Long term memory lookup failed: {e.Message}");
            return empty;
        }
    }
}
