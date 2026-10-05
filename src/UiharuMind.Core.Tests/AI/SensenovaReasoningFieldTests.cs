using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.Tests.AI;

// 探针：商汤 sensenova-6.8-flash-lite 把思考放在 `reasoning` 字段（见官方示例），
// 而非常见的 `reasoning_content`。走真实管线（清洗 + SDK 解析）看思考到不到得了界面。
public class SensenovaReasoningFieldTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static IChatClient PipelineClient(string mediaType, string body)
    {
        var handler = new OpenAICompatibleHttpHandler(
            "https://token.sensenova.cn/v1/chat/completions",
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            }));
        return OpenAICompatibleChatClient.Create(handler, null, "sensenova-6.8-flash-lite", "probe");
    }

    private static ChatMessage UserMsg() => new(ChatRole.User, "介绍一下商汤科技。");

    [Fact]
    public async Task Streaming_ReasoningField_IsItSurfaced()
    {
        const string sse =
            "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"sensenova-6.8-flash-lite\"," +
            "\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"reasoning\":\"用户让我\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"sensenova-6.8-flash-lite\"," +
            "\"choices\":[{\"index\":0,\"delta\":{\"reasoning\":\"介绍商汤\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"sensenova-6.8-flash-lite\"," +
            "\"choices\":[{\"index\":0,\"delta\":{\"content\":\"商汤科技\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"sensenova-6.8-flash-lite\"," +
            "\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]," +
            "\"usage\":{\"prompt_tokens\":24,\"completion_tokens\":1606,\"total_tokens\":1630," +
            "\"completion_tokens_details\":{\"reasoning_tokens\":236}}}\n\n" +
            "data: [DONE]\n\n";

        var reasoning = new StringBuilder();
        var text = new StringBuilder();
        await foreach (ChatResponseUpdate update in PipelineClient("text/event-stream", sse)
                           .GetStreamingResponseAsync(UserMsg(), cancellationToken: TestContext.Current.CancellationToken))
        {
            foreach (AIContent content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent rc: reasoning.Append(rc.Text); break;
                    case TextContent tc: text.Append(tc.Text); break;
                }
            }
        }

        Assert.Equal("用户让我介绍商汤", reasoning.ToString());
        Assert.Equal("商汤科技", text.ToString());
    }

    [Fact]
    public async Task NonStreaming_ReasoningField_IsItSurfaced()
    {
        const string json =
            "{\"id\":\"1\",\"created\":1,\"model\":\"sensenova-6.8-flash-lite\",\"object\":\"chat.completion\"," +
            "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"商汤科技\",\"reasoning\":\"用户让我\"},\"finish_reason\":\"stop\"}]," +
            "\"usage\":{\"prompt_tokens\":24,\"completion_tokens\":1606,\"total_tokens\":1630}}";

        ChatResponse response = await PipelineClient("application/json", json)
            .GetResponseAsync(UserMsg(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(response.Messages.SelectMany(m => m.Contents),
            c => c is TextReasoningContent rc && rc.Text == "用户让我");
    }
}
