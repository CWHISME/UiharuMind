using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 编号对账靠「同一条回复里的调用同属一轮」认开局快照：这条落在框架行为上，换框架版本时由这里报警
/// </summary>
public class BrowserRoundTests
{
    [Fact]
    public async Task CallsInOneReply_ShareARound_AndTheNextReplyStartsANewOne()
    {
        List<(string CallId, object? Round)> seen = new();
        AITool tool = AIFunctionFactory.Create(() =>
        {
            lock (seen) seen.Add((FunctionInvokingChatClient.CurrentContext!.CallContent.CallId, PuppeteerTab.CurrentRound()));
            return "ok";
        }, "probe");

        using FunctionInvokingChatClient client = new(new ScriptedClient(["a", "b"], ["c"]))
        {
            AllowConcurrentInvocation = true,
        };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], new ChatOptions { Tools = [tool] },
            TestContext.Current.CancellationToken);

        Dictionary<string, object?> rounds = seen.ToDictionary(entry => entry.CallId, entry => entry.Round);
        Assert.NotNull(rounds["a"]);
        Assert.Equal(rounds["a"], rounds["b"]);
        Assert.NotEqual(rounds["a"], rounds["c"]);
    }

    [Fact]
    public void OutsideAToolCall_ThereIsNoRound()
    {
        Assert.Null(PuppeteerTab.CurrentRound());
    }

    // 按脚本逐条回复：每条是一批并行的工具调用，脚本用完就回一句话收尾
    private sealed class ScriptedClient(params string[][] replies) : IChatClient
    {
        private int _next;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ChatMessage reply = _next < replies.Length
                ? new ChatMessage(ChatRole.Assistant,
                    replies[_next++].Select(id => (AIContent)new FunctionCallContent(id, "probe")).ToList())
                : new ChatMessage(ChatRole.Assistant, "done");
            return Task.FromResult(new ChatResponse(reply));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
