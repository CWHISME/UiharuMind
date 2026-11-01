using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 【验证用，临时】注入的消息落盘后还是不是<b>同一个实例</b>，自己打的附加键会不会跟着走。
///
/// 这两件事决定界面能不能用「按引用认」判断一条历史消息是否已经由内容流画过：
/// 能，补画前一句 ReferenceEquals 就够，不必按正文或时间戳去重；
/// 不能，就得给消息一个稳定身份（附加键）而附加键本身必须能活过落盘。
/// </summary>
public class InjectedMessageHistoryIdentityTests : IDisposable
{
    private const string ProbeKey = "_probeStreamRendered";

    private readonly List<string> _sessions = [];

    public void Dispose()
    {
        foreach (string id in _sessions) SessionManager.Instance.Delete(id);
    }

    [Fact]
    public async Task InjectedMessage_IdentityAndAnnotations_SurvivePersistence()
    {
        ChatSession chat = new() { Title = "身份探针" };
        SessionManager.Instance.Add(chat);
        _sessions.Add(chat.SessionId);

        ChatMessage injected = new(ChatRole.User, "插一句")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ProbeKey] = true },
        };

        RecordingChatClient leaf = new();
        ServiceCallSignalingChatClient signal = new(leaf);
        HarnessAgentOptions options = AgentOptionsFactory.CreateSubAgentBaseOptions(
            HistoryCompaction.Create(() => 8_000, new TurnInputEstimate()));
        options.ChatHistoryProvider = new SessionChatHistoryProvider();
        options.DisableToolAutoApproval = true;

        AIAgent agent = signal.AsHarnessAgent(options);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        SessionChatHistoryProvider.Bind(session, chat.SessionId);
        MessageInjectingChatClient injector = agent.GetService<MessageInjectingChatClient>()!;

        await injector.EnqueueMessagesAsync(session, [injected], TestContext.Current.CancellationToken);
        await agent.RunAsync("你好", session, cancellationToken: TestContext.Current.CancellationToken);

        ChatMessage stored = Assert.Single(chat.History, x => x.Role == ChatRole.User && x.Text == "插一句");
        string shape = $"sameInstance={ReferenceEquals(stored, injected)} " +
                       $"keySurvived={stored.AdditionalProperties?.ContainsKey(ProbeKey) == true}";

        // 两条事实：落盘的就是注入那个实例（界面的引用判据因此成立），附加键随消息一起活到历史
        Assert.True(ReferenceEquals(stored, injected), shape);
        Assert.True(stored.AdditionalProperties?.ContainsKey(ProbeKey) == true, shape);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "好")));

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
