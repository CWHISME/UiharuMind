using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 上下文注入（世界设定、知识库）以 <b>User</b> 消息追加在末位，而压缩只保留最近两组。
/// 若框架把每条 user 消息都算一组，两条注入就会把本轮那条带图提问挤出保留窗——
/// 「这张图已经在你眼前」的判据（<c>TurnAttachedImages</c>）于是会说一句不成立的话。
///
/// 不靠读框架文档判断：预置一份已经贴着截断水位的历史，挂两个注入 provider，
/// 跑一发带图提问，看最内层客户端收到的请求里那张图还在不在。
/// 前提是这一发真的被裁过——所以先断言请求比历史短，否则「图还在」不说明任何事。
/// </summary>
public class InjectedContextTruncationTests : IDisposable
{
    private const int ContextLength = 8_000; //预算约 7k token
    private const int SeededMessages = 40; //每条约 1500 token，远超水位：第一发就必须截断

    private readonly List<string> _sessions = [];

    public void Dispose()
    {
        foreach (string id in _sessions) SessionManager.Instance.Delete(id);
    }

    [Fact]
    public async Task InjectedUserContextMessages_DoNotEvictThisTurnsImage()
    {
        ChatSession chat = new() { Title = "注入与截断" };
        SessionManager.Instance.Add(chat);
        _sessions.Add(chat.SessionId);
        for (int i = 0; i < SeededMessages; i++)
        {
            chat.History.Add(new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                $"{i}: {new string('x', 1_500)}"));
        }

        TurnInputEstimate estimate = new();
        HarnessAgentOptions options =
            AgentOptionsFactory.CreateSubAgentBaseOptions(HistoryCompaction.Create(() => ContextLength, estimate));
        options.ChatHistoryProvider = new SessionChatHistoryProvider();
        options.DisableToolAutoApproval = true;
        options.AIContextProviders = [new InjectedProvider("[世界设定]"), new InjectedProvider("[知识片段]")];

        RecordingChatClient leaf = new();
        AIAgent agent = AgentAssembler.MoveCompactionToLeaf(leaf, options).AsHarnessAgent(options);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        SessionChatHistoryProvider.Bind(session, chat.SessionId);

        await agent.RunAsync(new ChatMessage(ChatRole.User,
            [
                new DataContent(TestImages.Png(4, 4), "image/png"),
                new TextContent("看这张"),
            ]), session, cancellationToken: TestContext.Current.CancellationToken);

        // 先证这一发真的被裁过，否则下面的「图还在」不说明任何事
        Assert.True(leaf.LastRequest.Count < SeededMessages / 2,
            $"最后一发带了 {leaf.LastRequest.Count} 条（历史预置 {SeededMessages} 条），看起来没触发截断");
        // 图与提问都在：注入的上下文消息没有把本轮挤掉
        Assert.Contains(leaf.LastRequest, message => message.Contents.OfType<DataContent>().Any());
        Assert.Contains(leaf.LastRequest, message => message.Text == "看这张");
    }

    /// <summary>像世界设定那样，以一条 User 消息注入在末位</summary>
    private sealed class InjectedProvider(string text) : AIContextProvider
    {
        public override IReadOnlyList<string> StateKeys => [];

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context,
            CancellationToken cancellationToken = default) =>
            new(new AIContext { Messages = [new ChatMessage(ChatRole.User, text)] });
    }

    private sealed class RecordingChatClient : IChatClient
    {
        /// <summary>最近一次请求带的消息</summary>
        public List<ChatMessage> LastRequest { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = messages.ToList();
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
