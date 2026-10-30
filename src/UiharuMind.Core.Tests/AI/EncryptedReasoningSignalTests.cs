using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Response;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加密思考的信号链路：流里看见 <c>reasoning.encrypted</c> → HTTP 层通知 → <see cref="LazyChatClient"/>
/// 转成一条内容 → 界面在<b>思考期</b>就开卡。
///
/// 钉住的是两端的接线，不是任何一端的内部逻辑：漏接时症状是"思考期一片空白、卡片等落盘才出现"，
/// 而那既编译得过、也测得过——只有把两端连起来断言才拦得住。
/// </summary>
public class EncryptedReasoningSignalTests
{
    /// <summary>一条流里加密条目出现多次，只通知一次（界面要的是一张卡，不是计数）</summary>
    [Fact]
    public async Task Stream_ReportsEncryptedReasoningOnce()
    {
        const string body =
            "data: {\"choices\":[{\"delta\":{\"reasoning_details\":[{\"type\":\"reasoning.encrypted\",\"data\":\"AAA\"}]}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"reasoning_details\":[{\"type\":\"reasoning.encrypted\",\"data\":\"BBB\"}]}}]}\n\n" +
            "data: [DONE]\n\n";
        int reported = 0;
        using MemoryStream inner = new(Encoding.UTF8.GetBytes(body));
        await using SseSanitizingStream stream = new(inner, null, _ => reported++);

        using StreamReader reader = new(stream);
        await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, reported);
    }

    /// <summary>没有加密条目的流一个信号都不发：普通思考/正文不该冒出一张空卡</summary>
    [Fact]
    public async Task Stream_WithoutEncryptedReasoning_ReportsNothing()
    {
        const string body =
            "data: {\"choices\":[{\"delta\":{\"content\":\"391\"}}]}\n\n" +
            "data: [DONE]\n\n";
        int reported = 0;
        using MemoryStream inner = new(Encoding.UTF8.GetBytes(body));
        await using SseSanitizingStream stream = new(inner, null, _ => reported++);

        using StreamReader reader = new(stream);
        await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, reported);
    }

    /// <summary>
    /// HTTP 层在深处调那个回调，客户端据此在本轮流里补一条 <see cref="EncryptedReasoningContent"/>。
    /// 回调传的是同一个对象，所以设置方（客户端）拿得到深处的调用结果——这是它不写成标志位的原因。
    /// </summary>
    [Fact]
    public async Task Client_EmitsTheSignalContent()
    {
        LazyChatClient client = new(() => Running(new SignalRaisingClient()));

        List<AIContent> seen = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.AddRange(update.Contents);
        }

        Assert.Single(seen.OfType<EncryptedReasoningContent>());
        Assert.Contains(seen, x => x is TextContent { Text: "391" }); //原有内容一条不少
    }

    private static ModelRunningData Running(IChatClient inner)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "signal-test",
                ModelId = "agnes-3.0-flash",
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    /// <summary>扮演 HTTP 层：先报"看见加密思考"，再吐一段正文</summary>
    private sealed class SignalRaisingClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("本测试只走流式路径");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            // 票据形态照 CommandCode 实测结果：一个 reasoning.encrypted 条目，data 是密文
            LlmRequestScope.Current.EncryptedReasoningObserved?.Invoke(
                """[{"type":"reasoning.encrypted","data":"Q-PaD"}]"""u8.ToArray());
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "391");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
