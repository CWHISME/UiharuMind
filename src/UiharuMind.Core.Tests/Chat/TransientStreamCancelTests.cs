using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 临时会话（提示词模板按需起的那一种）取消时补存进历史的内容。
///
/// <b>历史是一条消息对一次服务调用</b>：一轮里的每一发由历史提供器各落一条。
/// 从前取消时把<b>整轮</b>正文拼成一条再写一遍——前面几发已落过盘，于是历史里多出一条
/// 把几句发言粘在一起的重复假回复，形状与子代理停止那次一模一样。
/// </summary>
public class TransientStreamCancelTests
{
    private static ChatSession NewSession()
    {
        return new ChatSession("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
    }

    /// <summary>两发都落了盘，取消时没有「正在流的那段」：一个字都不该补</summary>
    [Fact]
    public async Task CancelledAfterEveryCallLanded_AppendsNoAssistantMessage()
    {
        ChatSession session = NewSession();
        session.History.Add(session.CreateMessage(ChatRole.User, "问一句")); //让「基于现有历史重生成」这条路能走通
        session.SetRunnerForTest(new ScriptedRunner(
            new TextContent("第一发"),
            MessageBoundaryContent.Instance, //这一发已落盘
            new TextContent("第二发"),
            MessageBoundaryContent.Instance));

        List<ChatMessage> written = await RunUntilCancelled(session, cancelAfter: 4);

        Assert.Empty(written);
    }

    /// <summary>停在第二发中间：只补这半截，不把第一发再拼一遍</summary>
    [Fact]
    public async Task CancelledMidCall_AppendsOnlyTheInFlightSegment()
    {
        ChatSession session = NewSession();
        session.History.Add(session.CreateMessage(ChatRole.User, "问一句"));
        session.SetRunnerForTest(new ScriptedRunner(
            new TextContent("已经落盘的第一发"),
            MessageBoundaryContent.Instance,
            new TextContent("正在流的第二发")));

        List<ChatMessage> written = await RunUntilCancelled(session, cancelAfter: 3);

        ChatMessage assistant = Assert.Single(written);
        Assert.Equal(ChatRole.Assistant, assistant.Role);
        Assert.Equal("正在流的第二发", assistant.Text);
    }

    /// <summary>输入已经在历史里（历史提供器在失败路径上补过）就不再写一条，避免同一句两条</summary>
    [Fact]
    public async Task Cancelled_DoesNotDuplicateTheInputAlreadyInHistory()
    {
        ChatSession session = NewSession();
        // 提供器落盘时用的是重建的副本：实例不同、正文相同
        session.History.Add(session.CreateMessage(ChatRole.User, "帮我看一下"));
        ChatMessage input = session.CreateMessage(ChatRole.User, "帮我看一下");
        session.SetRunnerForTest(new ScriptedRunner(new TextContent("半截")));

        List<ChatMessage> written = await RunUntilCancelled(session, input, cancelAfter: 1);

        ChatMessage assistant = Assert.Single(written);
        Assert.Equal(ChatRole.Assistant, assistant.Role);
    }

    /// <summary>输入确实没进历史（还没发出请求就被取消）时才补它，否则那句话会消失</summary>
    [Fact]
    public async Task Cancelled_WithInputNotInHistory_AppendsIt()
    {
        ChatSession session = NewSession();
        ChatMessage input = session.CreateMessage(ChatRole.User, "帮我看一下");
        session.SetRunnerForTest(new ScriptedRunner(MessageBoundaryContent.Instance));

        List<ChatMessage> written = await RunUntilCancelled(session, input, cancelAfter: 1);

        ChatMessage user = Assert.Single(written);
        Assert.Equal(ChatRole.User, user.Role);
        Assert.Equal("帮我看一下", user.Text);
    }

    /// <summary>跑一遍并在收到第 <paramref name="cancelAfter"/> 段内容时取消，返回新写进历史的那几条</summary>
    private static async Task<List<ChatMessage>> RunUntilCancelled(ChatSession session, int cancelAfter) =>
        await RunUntilCancelled(session, null, cancelAfter);

    private static async Task<List<ChatMessage>> RunUntilCancelled(ChatSession session, ChatMessage? input,
        int cancelAfter)
    {
        using CancellationTokenSource cancellation = new();
        int before = session.History.Count;
        int seen = 0;

        try
        {
            await foreach (AIContent content in session.GenerateCompletionStreamingContent(input,
                               cancellation.Token))
            {
                if (++seen == cancelAfter) cancellation.Cancel(); //模拟用户点停止
            }
        }
        catch (OperationCanceledException)
        {
            // 取消时迭代器可能直接抛：历史已在 finally 里补好
        }

        List<ChatMessage> written = new();
        for (int i = before; i < session.History.Count; i++) written.Add(session.History[i]);
        return written;
    }

    /// <summary>照给定的内容序列吐；每次取下一段前检查取消令牌，模拟「停在半路」</summary>
    private sealed class ScriptedRunner(params AIContent[] contents) : ICharacterRunner
    {
        public bool HasSession => true;

        public ETurnBusy Busy => ETurnBusy.None;

        public Action? BusyChanged { get; set; }

        public ChatOptions? ChatOptions => null;

        public Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (AIContent content in contents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return content;
                await Task.Yield();
            }
        }

        public Task SaveStateAsync() => Task.CompletedTask;

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public Task<EAgentMode> GetModeAsync() => Task.FromResult(EAgentMode.Execute);

        public Task SetModeAsync(EAgentMode mode) => Task.CompletedTask;

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages) => Task.FromResult(false);

        public Task<IReadOnlyCollection<ChatMessage>> CancelInjectionsAsync(
            IReadOnlyCollection<ChatMessage> messages) => Task.FromResult<IReadOnlyCollection<ChatMessage>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
