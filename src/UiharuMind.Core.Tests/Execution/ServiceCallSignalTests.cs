using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 插话气泡在服务调用发出那一刻定位，群轮在说完那一刻封口（见 <see cref="ServiceCallSignalingChatClient"/>）。
/// 走真实的 Harness 管线，只把最内层的模型换成记录器
/// </summary>
public class ServiceCallSignalTests
{
    /// <summary>
    /// 绕坑承重的框架行为：叶子被调用时，注入层已把队列排空、插话就在这次请求里。
    /// 哪天这条红了，说明框架改了排空时机，叶子报信就不再等于「被这次调用带走」
    /// </summary>
    [Fact]
    public async Task WhenTheLeafIsCalled_TheInjectionHasLeftTheQueue_AndRidesThisRequest()
    {
        RecordingChatClient leaf = new();
        ServiceCallSignalingChatClient signal = new(leaf);
        AIAgent agent = signal.AsHarnessAgent(Options());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        MessageInjectingChatClient injector = agent.GetService<MessageInjectingChatClient>()!;

        ChatMessage interjection = new(ChatRole.User, "插一句");
        await injector.EnqueueMessagesAsync(session, [interjection], TestContext.Current.CancellationToken);
        IReadOnlyList<ChatMessage>? pendingAtSignal = null;
        signal.Starting = async _ => pendingAtSignal = await injector.GetPendingMessagesAsync(session);

        await agent.RunAsync("你好", session, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(pendingAtSignal);
        Assert.Empty(pendingAtSignal);
        Assert.Contains(leaf.LastRequest, x => ReferenceEquals(x, interjection));
    }

    /// <summary>
    /// 群轮封口承重的框架行为（ADR 0049 修订）：模型说完时队列里有插话，注入层会续一次调用；
    /// 在「说完」那一声里撤掉，就不续。哪天前一半红了，说明框架不再续轮，封口可以删；
    /// 后一半红了，说明框架改了看队列的时机，封口撤得太晚
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InterjectionPendingWhenTheReplyFinishes_ContinuesTheTurn_UnlessWithdrawnOnFinishing(
        bool withdraw, bool streaming)
    {
        RecordingChatClient leaf = new();
        ServiceCallSignalingChatClient signal = new(leaf);
        AIAgent agent = signal.AsHarnessAgent(Options());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        MessageInjectingChatClient injector = agent.GetService<MessageInjectingChatClient>()!;

        // 在第一次调用已发出、队列已排空之后插进来：正是「别人在他说最后一句时发言」
        ChatMessage interjection = new(ChatRole.User, "[Bob]: 插一句");
        signal.Starting = async token =>
        {
            if (leaf.Calls == 0) await injector.EnqueueMessagesAsync(session, [interjection], token);
        };
        int finished = 0;
        signal.Finishing = async _ =>
        {
            finished++;
            if (withdraw) await PendingInjectionQueueAccess.RemoveAsync(injector, session, interjection);
        };

        if (streaming)
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("你好", session,
                               cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        }
        else
        {
            await agent.RunAsync("你好", session, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(withdraw ? 1 : 2, leaf.Calls);
        Assert.Equal(leaf.Calls, finished);
    }

    /// <summary>带要执行的工具调用的那次不算说完：工具循环还要接着调，插话本来就会在下一次被带走</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AResponseWithAFunctionCall_IsNotFinishing(bool streaming)
    {
        RecordingChatClient leaf = new()
        {
            Reply = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Read")]),
        };
        int finished = 0;
        ServiceCallSignalingChatClient signal = new(leaf) { Finishing = _ => Task.FromResult(finished++) };
        List<ChatMessage> request = [new(ChatRole.User, "hi")];

        if (streaming)
        {
            await foreach (ChatResponseUpdate _ in signal.GetStreamingResponseAsync(request,
                               cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        }
        else
        {
            await signal.GetResponseAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, finished);
    }

    /// <summary>报信只是界面时机，它失败了请求照发</summary>
    [Fact]
    public async Task AFailingSignal_DoesNotFailTheRequest()
    {
        RecordingChatClient leaf = new();
        ServiceCallSignalingChatClient signal = new(leaf) { Starting = _ => throw new InvalidOperationException("boom") };

        ChatResponse response = await signal.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("好", response.Text);
    }

    private static HarnessAgentOptions Options()
    {
        HarnessAgentOptions options =
            AgentOptionsFactory.CreateSubAgentBaseOptions(HistoryCompaction.Create(() => 8_000, new TurnInputEstimate()));
        options.ChatHistoryProvider = new InMemoryChatHistoryProvider();
        options.DisableToolAutoApproval = true;
        return options;
    }

    private sealed class RecordingChatClient : IChatClient
    {
        /// <summary>最近一次请求带的消息</summary>
        public List<ChatMessage> LastRequest { get; private set; } = [];

        /// <summary>收到过几次请求</summary>
        public int Calls { get; private set; }

        /// <summary>每次都回这一条；没给回一句「好」</summary>
        public ChatMessage? Reply { get; init; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = messages.ToList();
            Calls++;
            return Task.FromResult(new ChatResponse(Reply ?? new ChatMessage(ChatRole.Assistant, "好")));
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
