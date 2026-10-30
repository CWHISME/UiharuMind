using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加密票据的门禁：票据只回给产出它的那个模型。
///
/// 网关自己不校验（实测把 Meta 的票发给 deepseek 照样 200），所以这一侧没有兜底——
/// 漏了就会把 A 的密文当 B 的推理上下文发出去，而两条路都不会报错，只会安静地错。
/// </summary>
public class ReasoningTicketGateTests
{
    private const string Ticket =
        """[{"type":"reasoning.encrypted","data":"Q-PaD","id":"rs_1","format":"meta-responses-v1","index":0}]""";

    /// <summary>同一个模型：上一发拿到的票挂在最后一条助手消息上，随下一发送回去</summary>
    [Fact]
    public async Task Ticket_IsReplayedToTheSameModel()
    {
        TicketClient inner = new(Encoding.UTF8.GetBytes(Ticket));
        Queue<string> models = new(["m-a", "m-a"]);
        LazyChatClient client = new(() => Running(inner, models.Dequeue()));

        await Ask(client, "hi");
        await Ask(client, "再来一次");

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

        await Ask(client, "hi");
        await Ask(client, "换个模型再问");

        Assert.Null(inner.SeenTickets);
    }

    /// <summary>用一次即消费：票只用在其后紧跟的那一发，不会一直挂在后面每一轮</summary>
    [Fact]
    public async Task Ticket_IsConsumedOnce()
    {
        TicketClient inner = new(Encoding.UTF8.GetBytes(Ticket));
        Queue<string> models = new(["m-a", "m-a", "m-a"]);
        LazyChatClient client = new(() => Running(inner, models.Dequeue()));

        await Ask(client, "hi");
        await Ask(client, "第二轮");
        await Ask(client, "第三轮");

        Assert.Null(inner.SeenTickets); //第三发看到的也是空
    }

    private static Task Ask(LazyChatClient client, string text) =>
        client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, text), new ChatMessage(ChatRole.Assistant, "ok"),
             new ChatMessage(ChatRole.User, "继续")],
            cancellationToken: TestContext.Current.CancellationToken);

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
