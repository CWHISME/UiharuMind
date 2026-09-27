using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 压缩要真的落到发出去的请求上。走真实的 Harness 管线，只把最内层的模型换成记录器：
/// Harness 恒开逐次落盘，会话第一次服务调用后就带上本地哨兵 ConversationId，
/// 框架的 CompactionProvider 见它就当成服务端托管、整段跳过（见 <c>AgentAssembler.MoveCompactionToLeaf</c>）
/// </summary>
public class HarnessCompactionTests
{
    private const int ContextLength = 8_000; //预算约 7k token
    private const int Turns = 24; //截断恒保留最近 32 组：轮数要多过它才有得裁；每轮 1.5k 字，合计约 9k token，过截断水位

    [Fact]
    public async Task CompactionOnTheLeaf_TrimsEveryCall_AndRecordsTheHistoryEstimate()
    {
        TurnInputEstimate estimate = new();
        HarnessAgentOptions options = Options(estimate);
        RecordingChatClient leaf = new();

        AIAgent agent = AgentAssembler.MoveCompactionToLeaf(leaf, options).AsHarnessAgent(options);
        await TalkAsync(agent);

        Assert.True(leaf.LastRequestCount < Turns * 2 - 1, $"最后一次请求带了 {leaf.LastRequestCount} 条，没被裁");
        Assert.True(estimate.LastHistory > 0); //触发条件跑过才写得进来
    }

    /// <summary>
    /// 哨兵：框架原路至今仍跳过。哪天这条红了，说明框架认得本地哨兵了，
    /// <c>MoveCompactionToLeaf</c> 那段绕坑就可以删
    /// </summary>
    [Fact]
    public async Task FrameworkCompactionProvider_StillSkipsLocalHistorySessions()
    {
        TurnInputEstimate estimate = new();
        HarnessAgentOptions options = Options(estimate);
        RecordingChatClient leaf = new();

        AIAgent agent = leaf.AsHarnessAgent(options);
        await TalkAsync(agent);

        Assert.Equal(Turns * 2 - 1, leaf.LastRequestCount);
    }

    private static HarnessAgentOptions Options(TurnInputEstimate estimate)
    {
        HarnessAgentOptions options =
            AgentOptionsFactory.CreateSubAgentBaseOptions(HistoryCompaction.Create(() => ContextLength, estimate));
        options.ChatHistoryProvider = new InMemoryChatHistoryProvider();
        options.DisableToolAutoApproval = true;
        return options;
    }

    private static async Task TalkAsync(AIAgent agent)
    {
        AgentSession session = await agent.CreateSessionAsync();
        for (int i = 0; i < Turns; i++)
        {
            await agent.RunAsync($"{i}: {new string('x', 1_500)}", session,
                cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        /// <summary>最近一次请求带了几条消息</summary>
        public int LastRequestCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequestCount = messages.Count();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "好")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (ChatResponseUpdate update in response.ToChatResponseUpdates()) yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
