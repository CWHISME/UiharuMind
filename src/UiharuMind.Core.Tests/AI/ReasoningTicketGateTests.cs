using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加密票据的门禁：只回给产出它的模型，且只在历史恰好长了一条助手消息时回。
///
/// 两个条件都不是形式主义。网关自己不校验（实测把 Meta 的票发给 deepseek 照样 200），
/// 而服务端认的是「这条 reasoning item 就挂在这条回复前面」——挂错了会 400，
/// 比不带票更糟。所以漏掉任何一条都不会报错，只会安静地错。
/// </summary>
public class ReasoningTicketGateTests
{
    private const string Ticket =
        """[{"type":"reasoning.encrypted","data":"Q-PaD","id":"rs_1","format":"meta-responses-v1","index":0}]""";

    /// <summary>同一个模型、历史长了一条：上一发的票挂在那条新回复上，随下一发送回去</summary>
    [Fact]
    public async Task Ticket_IsReplayedToTheSameModel()
    {
        TicketClient inner = new(Encoding.UTF8.GetBytes(Ticket));
        Queue<string> models = new(["m-a", "m-a"]);
        LazyChatClient client = new(() => Running(inner, models.Dequeue()));

        List<ChatMessage> history = Conversation();
        await Ask(client, history);
        Reply(history); //第一发的回复落成消息
        await Ask(client, history);

        Assert.NotNull(inner.SeenTickets);
        Assert.Equal(Ticket, inner.SeenTickets[^1]);
    }

    /// <summary>换了模型：票当场作废，一条都不发（票据只对产出它的模型有意义）</summary>
    [Fact]
    public async Task Ticket_IsDroppedWhenTheModelChanges()
    {
        TicketClient inner = new(Encoding.UTF8.GetBytes(Ticket));
        Queue<string> models = new(["m-a", "m-b"]);
        LazyChatClient client = new(() => Running(inner, models.Dequeue()));

        List<ChatMessage> history = Conversation();
        await Ask(client, history);
        Reply(history);
        await Ask(client, history);

        Assert.Null(inner.SeenTickets);
    }

    /// <summary>
    /// 上一发拿到了票但没落成消息（失败/中断）：票据滞留，此时历史没长，
    /// 挂上去就会贴到一条与它无关的回复上——必须丢掉。
    /// </summary>
    [Fact]
    public async Task Ticket_IsDroppedWhenHistoryDidNotGrow()
    {
        TicketClient inner = new(Encoding.UTF8.GetBytes(Ticket));
        Queue<string> models = new(["m-a", "m-a"]);
        LazyChatClient client = new(() => Running(inner, models.Dequeue()));

        List<ChatMessage> history = Conversation();
        await Ask(client, history);
        await Ask(client, history); //同样的历史再发一次：没有新回复可挂

        Assert.Null(inner.SeenTickets);
    }

    /// <summary>用一次即消费：票只用在其后紧跟的那一发，不会一直挂在后面每一轮</summary>
    [Fact]
    public async Task Ticket_IsConsumedOnce()
    {
        TicketClient inner = new(Encoding.UTF8.GetBytes(Ticket));
        Queue<string> models = new(["m-a", "m-a", "m-a"]);
        LazyChatClient client = new(() => Running(inner, models.Dequeue()));

        List<ChatMessage> history = Conversation();
        await Ask(client, history);
        Reply(history);
        await Ask(client, history); //这一发把票用掉
        Reply(history);
        await Ask(client, history);

        Assert.Null(inner.SeenTickets); //第三发看到的是空
    }

    private static List<ChatMessage> Conversation() => [new ChatMessage(ChatRole.User, "hi")];

    private static void Reply(List<ChatMessage> history) => history.Add(new ChatMessage(ChatRole.Assistant, "ok"));

    private static Task Ask(LazyChatClient client, IEnumerable<ChatMessage> history) =>
        client.GetResponseAsync(history, cancellationToken: TestContext.Current.CancellationToken);

    private static ModelRunningData Running(IChatClient inner, string modelId)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "ticket-test",
                ModelId = modelId,
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    /// <summary>
    /// 扮演 HTTP 层：记下发请求那一刻拿到的票据清单，顺便按剧本交出一张新票。
    /// 票据清单在这里可见，正是因为它就摆在请求上下文上——HTTP 层读到的就是这一份。
    /// </summary>
    private sealed class TicketClient(byte[]? emit) : IChatClient
    {
        private byte[]? _emit = emit; //只交一次票：真实链路里一个模型也不是每一发都给新票

        public IReadOnlyList<string?>? SeenTickets { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SeenTickets = LlmRequestScope.Current.ReasoningTickets;
            if (_emit is { } ticket)
            {
                _emit = null;
                LlmRequestScope.Current.EncryptedReasoningObserved?.Invoke(ticket);
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试只走非流式路径");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
