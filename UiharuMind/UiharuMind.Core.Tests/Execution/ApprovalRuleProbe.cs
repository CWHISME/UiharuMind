using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 把一组自动放行规则跑给一次工具调用看。
/// 规则是「或」语义：任一条放行即放行——探针必须和框架同一口径，否则测出来的是另一套系统。
/// </summary>
internal static class ApprovalRuleProbe
{
    /// <summary>
    /// 这次调用会被自动放行吗
    /// </summary>
    /// <param name="rules">规则集</param>
    /// <param name="call">工具调用</param>
    /// <returns>true 表示无需用户审批</returns>
    public static async Task<bool> IsApprovedAsync(
        IEnumerable<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules, FunctionCallContent call)
    {
        // 1.16 起规则收上下文对象;用最小桩 agent/session 构造之
        ChatClientAgent agent = new(new NullChatClient());
        AgentSession session = await agent.CreateSessionAsync();
        ToolAutoApprovalRuleContext context = new(call, agent, session, [], null);
        foreach (var rule in rules)
        {
            if (await rule(context)) return true;
        }

        return false;
    }

    private sealed class NullChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
