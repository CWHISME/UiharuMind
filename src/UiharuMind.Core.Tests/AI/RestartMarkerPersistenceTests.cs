using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 断流重发在<b>落盘侧</b>留下的残影。
///
/// <para>
/// 重发标记（<see cref="StreamRestartContent"/>）不带 <c>MessageId</c>，而框架按 id 聚合增量：
/// 它会把标记并进<b>被作废的那一发</b>的消息里。于是历史里多出一条只带思考的助手消息，
/// 重开会话就多一张思考卡——真实档案里 18 个会话共 57 处，其中 51 处回放时可见。
/// </para>
/// <para>
/// 这里用真的 <see cref="ChatClientAgent"/> 跑一遍聚合，钉住「作废的那一发整条不落盘」。
/// </para>
/// </summary>
public class RestartMarkerPersistenceTests
{
    /// <summary>第一发有内容、被重发作废：整条丢掉，只留重发那一发</summary>
    [Fact]
    public async Task SupersededAttempt_IsNotPersisted()
    {
        ChatSession session = await RunTurn(new RestartClient(abortedHasContent: true));

        ChatMessage stored = Assert.Single(AssistantMessages(session));
        Assert.Equal("第二发的思考", Assert.IsType<TextReasoningContent>(stored.Contents[0]).Text);
        Assert.DoesNotContain(stored.Contents, x => x is StreamRestartContent);
    }

    /// <summary>
    /// 第一发一个字都没产出（标记并进了重发那条的开头）：只摘标记，那条消息本身要留下。
    /// 丢掉它等于把这一发整段回复吞掉
    /// </summary>
    [Fact]
    public async Task MarkerAtHead_KeepsTheSurvivingMessage()
    {
        ChatSession session = await RunTurn(new RestartClient(abortedHasContent: false));

        ChatMessage stored = Assert.Single(AssistantMessages(session));
        Assert.Single(stored.Contents);
        Assert.IsType<TextReasoningContent>(stored.Contents[0]);
    }

    private static List<ChatMessage> AssistantMessages(ChatSession session) =>
        session.History.Where(x => x.Role == ChatRole.Assistant).ToList();

    private static async Task<ChatSession> RunTurn(IChatClient client)
    {
        ChatSession session = new("restart-persist", new CharacterData { CharacterId = "restart-persist" })
        {
            IsTransient = true,
        };
        // 历史提供器靠会话标识反查会话本体，所以它得在登记处里
        SessionManager.Instance.Add(session);
        SessionChatHistoryProvider provider = new();
        ChatClientAgent agent = new(client, new ChatClientAgentOptions
        {
            Name = "restart-persist",
            ChatOptions = new ChatOptions { Instructions = "x" },
            ChatHistoryProvider = provider,
        });
        AgentSession agentSession = await agent.CreateSessionAsync();
        SessionChatHistoryProvider.Bind(agentSession, session.SessionId);

        await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync(
                           new ChatMessage(ChatRole.User, "开工"), agentSession,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        return session;
    }

    /// <summary>
    /// 复现 <c>LazyChatClient</c> 的重发形状：第一发（可能带内容）→ 无 id 的重发标记 → 第二发。
    /// 标记不带 id 正是关键——框架因此把它并进第一发那条消息
    /// </summary>
    private sealed class RestartClient(bool abortedHasContent) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (abortedHasContent)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new TextReasoningContent("第一发的思考")]) { MessageId = "attempt-1" };
                await Task.Yield();
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, [StreamRestartContent.Instance]);
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new TextReasoningContent("第二发的思考")]) { MessageId = "attempt-2" };
            await Task.Yield();
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
