using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 记忆清单发送时才并进消息（ADR 0076）：历史里只有用户原话与注解，发出去的那份正文前面多一段清单
/// </summary>
public class MemoryIndexProjectionTests
{
    private const string List = "- a.md — 偏好";

    private static ChatMessage Anchored(string text)
    {
        ChatMessage message = new(ChatRole.User, text);
        message.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ChatMessageAnnotations.MemoryIndexSnapshot] = List,
        };
        return message;
    }

    [Fact]
    public void Project_MergesTheBlockIntoTheFirstText_AndLeavesHistoryAlone()
    {
        ChatMessage anchored = Anchored("开始干活");
        ChatMessage answer = new(ChatRole.Assistant, "好");

        IReadOnlyList<ChatMessage> sent = MemoryIndexProjection.Project([anchored, answer]);

        Assert.Equal($"{MemoryIndexProjection.Block(List)}\n\n开始干活", sent[0].Text);
        Assert.Single(sent[0].Contents.OfType<TextContent>());
        Assert.Same(answer, sent[1]);
        Assert.Equal("开始干活", anchored.Text);
    }

    [Fact]
    public void Project_NothingAnchored_ReturnsTheSameInstance()
    {
        List<ChatMessage> messages = [new(ChatRole.User, "你好")];

        Assert.Same(messages, MemoryIndexProjection.Project(messages));
    }

    [Fact]
    public void Project_ImageOnlyMessage_GetsATextBlockInFront()
    {
        ChatMessage anchored = new(ChatRole.User, [new DataContent(new byte[] { 1, 2, 3 }, "image/png")]);
        anchored.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ChatMessageAnnotations.MemoryIndexSnapshot] = List,
        };

        ChatMessage sent = MemoryIndexProjection.Project([anchored])[0];

        Assert.IsType<TextContent>(sent.Contents[0]);
        Assert.IsType<DataContent>(sent.Contents[1]);
    }

    /// <summary>旁路请求（写交接文档）与常规请求同样带着清单，前缀才中得了缓存</summary>
    [Fact]
    public async Task SideRequest_CarriesTheSameBlock()
    {
        List<ChatMessage> history = [Anchored("开始干活"), new(ChatRole.Assistant, "好")];

        IReadOnlyList<ChatMessage> supplied = await HistorySupply.ForSideRequestAsync(history, null, 20_000, 0,
            TestContext.Current.CancellationToken);

        Assert.StartsWith(MemoryIndexProjection.Block(List), supplied[0].Text);
    }

    /// <summary>
    /// 走真实 Harness 管线：快照随输入消息落盘，下一轮从历史里供给回来时仍在、清单只出现一次——
    /// 框架交给持久化的是重建的副本，注解要能跟着过去
    /// </summary>
    [Fact]
    public async Task ThroughTheHarness_TheSnapshotIsPersisted_AndProjectedOnce()
    {
        RecordingChatClient leaf = new();
        HarnessAgentOptions options = AgentOptionsFactory.CreateSubAgentBaseOptions(null);
        options.ChatHistoryProvider = new InMemoryChatHistoryProvider();
        options.DisableToolAutoApproval = true;
        AIAgent agent = new MemoryIndexProjectingChatClient(leaf).AsHarnessAgent(options);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        CancellationToken ct = TestContext.Current.CancellationToken;

        await agent.RunAsync([Anchored("开始干活")], session, cancellationToken: ct);
        await agent.RunAsync([new ChatMessage(ChatRole.User, "接着说")], session, cancellationToken: ct);

        List<ChatMessage> secondRequest = leaf.Requests[^1];
        ChatMessage first = secondRequest.First(x => x.Role == ChatRole.User);
        Assert.Equal($"{MemoryIndexProjection.Block(List)}\n\n开始干活", first.Text);
        Assert.Single(secondRequest, x => x.Text.Contains("[记忆清单]", StringComparison.Ordinal));
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = new();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
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
