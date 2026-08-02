using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Agent;
using UiharuMind.Core.Core.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死 RunTextAsync 产出的是<b>增量</b>而不是累积全文。
/// 快捷工具的窗口（QuickChatResultWindow / TranslationWindow）是把收到的字符串
/// AppendContent 追加上去的；一旦这里改成累积全文，窗口会把每次的全文再追加一遍，
/// 表现为内容重复且随长度二次膨胀。
/// </summary>
public class RunTextSemanticsTests
{
    [Fact]
    public async Task RunTextAsync_YieldsDeltasNotAccumulatedText()
    {
        StubRunner runner = new([
            new TextContent("你"),
            new TextReasoningContent("(思考过程不该出现在正文里)"),
            new TextContent("好"),
            new FunctionCallContent("c1", "tool", null),
            new TextContent("呀"),
        ]);

        List<string> chunks = [];
        await foreach (string chunk in runner.RunTextAsync([]))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(["你", "好", "呀"], chunks);
        // 追加式消费方拼出来的结果必须等于原文
        Assert.Equal("你好呀", string.Concat(chunks));
    }

    private sealed class StubRunner(IReadOnlyList<AIContent> contents) : ICharacterRunner
    {
        public bool HasSession => true;

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (AIContent content in contents)
            {
                yield return content;
            }

            await Task.CompletedTask;
        }

        public Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveStateAsync() => Task.CompletedTask;

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public EAgentMode GetMode() => EAgentMode.Execute;

        public void SetMode(EAgentMode mode)
        {
        }

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public bool TryInject(IEnumerable<ChatMessage> messages) => false;

        public ValueTask DisposeAsync() => default;
    }
}
