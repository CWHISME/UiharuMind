using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Harness;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住「工具执行时本轮用户消息已经落盘」这个次序——它是一条隐性依赖：
/// 「这张图已经在你眼前」的判据（<c>TurnAttachedImages</c>）靠它读会话历史，
/// 而历史由 <see cref="SessionChatHistoryProvider"/> 在<b>每次服务调用</b>之后追加。
///
/// 次序若不成立，判据只是永远不命中（退回从前的重复读），不会错拦——
/// 但那就等于这层拦截白做，所以在这里量一次，而不是靠注释推断。
/// </summary>
public class ToolSeesPersistedTurnInputTests : IDisposable
{
    private readonly List<string> _sessions = [];

    public void Dispose()
    {
        foreach (string id in _sessions) SessionManager.Instance.Delete(id);
    }

    [Fact]
    public async Task ToolInvocation_SeesThisTurnsUserMessageInHistory()
    {
        ChatSession chat = new() { Title = "落盘次序探针" };
        SessionManager.Instance.Add(chat);
        _sessions.Add(chat.SessionId);

        List<ChatMessage> atToolTime = [];
        AIFunction probe = AIFunctionFactory.Create(() =>
        {
            atToolTime.AddRange(chat.History.Snapshot());
            return "ok";
        }, "probe");

        AIAgent agent = Harness(probe, new ToolCallingClient());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        SessionChatHistoryProvider.Bind(session, chat.SessionId);
        await agent.RunAsync("看这张图", session, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(atToolTime, message => message.Role == ChatRole.User && message.Text == "看这张图");
    }

    private static AIAgent Harness(AIFunction probe, IChatClient leaf)
    {
        HarnessAgentOptions options = AgentOptionsFactory.CreateSubAgentBaseOptions(null);
        options.ChatHistoryProvider = new SessionChatHistoryProvider();
        options.ChatOptions ??= new ChatOptions();
        options.ChatOptions.Tools = [probe];
        return new ServiceCallSignalingChatClient(leaf).AsHarnessAgent(options);
    }

    /// <summary>第一发叫一次探针工具，第二发收尾</summary>
    private sealed class ToolCallingClient : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(Next()));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (ChatResponseUpdate update in response.ToChatResponseUpdates()) yield return update;
        }

        private ChatMessage Next() => ++_calls == 1
            ? new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("c1", "probe", new Dictionary<string, object?>())])
            : new ChatMessage(ChatRole.Assistant, "看过了");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
