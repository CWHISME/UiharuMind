/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.AI.Net.Request;

// 这个文件同时用到 OpenAI.Chat 与 M.E.AI，ChatMessage 两边都有——显式指明用 M.E.AI 那个
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.AI.Net;

// SDK 把 responses 线标为实验性 API（OPENAI001）。这条线是 ADR 0100 定的增量路，
// 用它是为了让加密思考票据由 SDK 原样往返（实测 ProtectedData → encrypted_content 逐字节不变）。
#pragma warning disable OPENAI001

/// <summary>
/// 走 OpenAI 兼容端点的聊天客户端（本地 llama.cpp 服务与远程模型共用）
/// </summary>
internal static class ChatClientFactory
{
    /// <summary>
    /// 建客户端：请求体由 <see cref="OpenAICompatibleRequestPolicy"/> 每次调用改写一次（重试不重做），
    /// HTTP 层只改写地址、记录并清洗响应
    /// </summary>
    /// <param name="handler">指向端点的 HTTP 层</param>
    /// <param name="model">目标模型，决定请求体怎么改写</param>
    /// <param name="modelId">请求里的模型名</param>
    /// <param name="apiKey">密钥</param>
    /// <param name="retryPolicy">重试策略；为 null 时用 SDK 默认</param>
    /// <param name="sendGate">共用限额的发送闸；为 null 时不排队</param>
    /// <returns>聊天客户端</returns>
    public static IChatClient Create(WireHttpHandler handler, ILlmModel? model, string modelId, string apiKey,
        ClientRetryPolicy? retryPolicy = null, SendGate? sendGate = null)
    {
        // 三条线的请求体形状各不相同，出口这一道是共同的：工具结果的形态在这里统一。
        // 工具的来源太多（ToolJson、框架的 AsAIFunction、MCP、以后还会加），逐个工具补一道补不全，
        // 而发出去的每一发都必经过这里
        return new ToolResultShapeChatClient(
            CreateProtocolClient(handler, model, modelId, apiKey, retryPolicy, sendGate));
    }

    /// <summary>按协议线建客户端；协议无关的那道闸由 <see cref="Create"/> 套在外面</summary>
    private static IChatClient CreateProtocolClient(WireHttpHandler handler, ILlmModel? model, string modelId,
        string apiKey, ClientRetryPolicy? retryPolicy, SendGate? sendGate)
    {
        // responses 线走另一个客户端：请求体形状（input item 数组）与响应形状（SSE 事件）都不同，
        // 改写器与解析器整套换掉。SDK 已经把这两件事做完了（ToOpenAIResponseItems / 事件翻译），
        // 自己手写一遍只会多一处要维护的协议实现。
        if (model?.ApiFormat == EApiFormat.Responses)
            return CreateResponsesClient(NewHttpClient(handler), modelId, apiKey, retryPolicy, sendGate);

        // Anthropic Messages 线同理：请求体（system 提到顶层、content 变 block 数组）与响应
        // （逐类 SSE 事件）都与 chat 不同，整套翻译交给官方 SDK。
        if (model?.ApiFormat == EApiFormat.Messages)
            return CreateMessagesClient(handler, model, modelId, apiKey, sendGate);

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(NewHttpClient(handler)),
            // 流式响应的读闸默认 100s:思考期 chunk 间隔一长就被 ReadTimeoutStream 掐断,
            // 且那异常在 HTTP 200 之后冒出,WireHttpHandler 看不到,只会被上层当用户取消吞掉。
            // 读卡的裁决完全交给上层 CancellationToken(用户停止按钮),这里不设静态时限
            NetworkTimeout = Timeout.InfiniteTimeSpan,
        };
        if (retryPolicy != null) options.RetryPolicy = retryPolicy;
        options.AddPolicy(new OpenAICompatibleRequestPolicy(model), PipelinePosition.PerCall);
        if (sendGate != null) options.AddPolicy(new SendGatePolicy(sendGate), PipelinePosition.PerTry);

        // 思考回填挂在客户端外面（ADR 0101）：历史里的思考绑到它自己那条消息上，
        // 不经过字节层，也就不存在「第几条助手消息」这个约定
        return new ReasoningReplayChatClient(
            new ChatClient(modelId, new ApiKeyCredential(apiKey), options).AsIChatClient(), model);
    }

    /// <summary>
    /// 建发请求用的 <see cref="HttpClient"/>。
    ///
    /// <para>Timeout 必须关掉：默认 100s 只管到响应头/首字节，SDK 自带客户端反而设成 Infinite。
    /// 流式长思考会撞它，一并关掉，裁决交给上层 CancellationToken。</para>
    ///
    /// <para><b>发送闸不在这里</b>：chat 与 responses 两条线的闸挂在 SDK 管道上
    /// （<see cref="SendGatePolicy"/>），Anthropic 线挂不到那条管道，改挂在 HTTP 层
    /// （<see cref="SendGateHandler"/>）。</para>
    /// </summary>
    private static HttpClient NewHttpClient(WireHttpHandler handler) =>
        new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// 建 responses 线的客户端。
    ///
    /// 两处设置与票据直接相关：
    /// - <c>store: false</c>：不让服务端存我们的响应。有状态续接（<c>previous_response_id</c>）
    ///   在 store 为真时才可用，而那意味着推理链存在服务端、我们手里没有密文——
    ///   票据机制的前提是「客户端保管密文、原样带回」，所以必须关掉存储。
    /// - <c>include: ["reasoning.encrypted_content"]</c>：显式要加密思考。
    ///   不显式要就只有思考 token 计数，密文不下发——那就没有票据可存。
    /// </summary>
    private static IChatClient CreateResponsesClient(HttpClient httpClient, string modelId, string apiKey,
        ClientRetryPolicy? retryPolicy, SendGate? sendGate)
    {
        var options = new ResponsesClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            NetworkTimeout = Timeout.InfiniteTimeSpan,
        };
        if (retryPolicy != null) options.RetryPolicy = retryPolicy;
        if (sendGate != null) options.AddPolicy(new SendGatePolicy(sendGate), PipelinePosition.PerTry);

        IChatClient client = new ResponsesClient(new ApiKeyCredential(apiKey), options).AsIChatClient(modelId);

        // store 与 include 挂在 RawRepresentationFactory 上：那是 SDK 唯一能改请求体的地方
        // （options 本身没有这两个字段——它们属于每次请求的 CreateResponseOptions）
        return client.AsBuilder().ConfigureOptions(x =>
        {
            // 先抓住原来的工厂：换成自己之后就没有「原来的」可调了，
            // 而配置动作里读 x.RawRepresentationFactory 拿到的是刚赋上去的那个——会自己调自己
            var previousFactory = x.RawRepresentationFactory;
            x.RawRepresentationFactory = state =>
            {
                var responseOptions = previousFactory?.Invoke(state) as CreateResponseOptions
                    ?? new CreateResponseOptions();
                responseOptions.StoredOutputEnabled = false;
                if (!responseOptions.IncludedProperties.Contains(IncludedResponseProperty.ReasoningEncryptedContent))
                    responseOptions.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);
                return responseOptions;
            };
        }).Build();
    }

    /// <summary>
    /// 建 Anthropic Messages 线的客户端。
    ///
    /// 四处与另两条线不同：
    ///
    /// <list type="number">
    /// <item><description><b>走裸 <see cref="HttpClient"/></b>：官方 SDK 不是 System.ClientModel 管道，
    /// 挂不进去 <see cref="OpenAICompatibleRequestPolicy"/>（那条是 chat 线的字节级改写器，
    /// Anthropic 的请求体由 SDK 自己造）。地址改写、亲和头、请求日志、响应清洗仍在
    /// <see cref="WireHttpHandler"/> 里——那个是协议无关的 HTTP 层。</description></item>
    /// <item><description><b>思考参数与缓存断点自己写</b>（<see cref="AnthropicRequestHandler"/>）：
    /// Anthropic 要的是 token 预算而不是档位名，<c>cache_control</c> 又要打在块上——
    /// 两者都够不着 SDK 的原生参数入口（<c>RawRepresentationFactory</c> 要的是整份参数，
    /// 而块是适配层内部造的）。</description></item>
    /// <item><description><b>发送闸挂在 HTTP 层</b>（<see cref="SendGateHandler"/>）而不是管道策略上，
    /// 理由见那里。</description></item>
    /// <item><description><b>思考块要先归一</b>（<see cref="AnthropicThinkingNormalizer"/>）：
    /// Anthropic 的签名与思考正文是同一个块的两个字段，而它的流式响应把两者拆成两帧发，
    /// 官方 SDK 原样交给两条思考内容。不合成一条，请求体里就成了两个块、头一个签名是空串，
    /// 服务端 400。</description></item>
    /// </list>
    /// </summary>
    private static IChatClient CreateMessagesClient(WireHttpHandler handler, ILlmModel? model,
        string modelId, string apiKey, SendGate? sendGate)
    {
        // 输出预算跟另外两条线同源：模型配置里显式填的 MaxTokens，没填就用协议默认。
        // 默认不能太小——Anthropic 的思考预算必须小于输出预算，而它自己的最小预算是 1024，
        // 输出预算比这更小就永远开不了思考
        int maxTokens = model is { MaxTokens: > 0 } ? model.MaxTokens : DefaultAnthropicMaxTokens;

        // 处理链由外到内：先排队（闸），再写思考参数、缓存断点与 metadata.user_id，最后是协议无关的 HTTP 层。
        // 断点清单与寿命来自与 chat 线同一张清单（RequestRewrite），各线只负责渲染；
        // 亲和开关与头那边同一个（SendsSessionAffinity），值是同一份会话哈希
        RequestRewrite rewrite = RequestRewrite.For(model, LlmRequestContext.Empty);
        HttpMessageHandler inner = new AnthropicRequestHandler(model?.ThinkingEffort, maxTokens,
            rewrite.CacheBreakpoints, rewrite.CacheTtl, model?.SendsSessionAffinity == true, handler);
        if (sendGate != null) inner = new SendGateHandler(sendGate, inner);
        HttpClient httpClient = new HttpClient(inner) { Timeout = Timeout.InfiniteTimeSpan };

        var options = new ClientOptions
        {
            ApiKey = apiKey,
            // 地址不在这儿给：HTTP 层每次发送前都改写成用户配置的那个端点
            // （WireHttpHandler.SendAsync），SDK 自己拼的地址会被覆盖掉
            HttpClient = httpClient,
            // 与另两条线同一口径：SDK 的默认超时是 10 分钟，而且它管到「读完响应体」为止——
            // 长思考 + 长正文的流式回复会被它拦腰掐断，异常还发生在响应头之后，上层只当用户取消。
            // 读卡的裁决完全交给上层 CancellationToken（停止按钮）
            Timeout = Timeout.InfiniteTimeSpan,
        };

        IChatClient client = new AnthropicClient(options).AsIChatClient(modelId, maxTokens);
        return new AnthropicThinkingChatClient(client);
    }

    /// <summary>
    /// Anthropic 线没填输出预算时的默认值。
    ///
    /// <b>为什么不用 SDK 的默认（1024）</b>：思考预算必须严格小于输出预算，
    /// 而 Anthropic 的最小思考预算是 1024——输出预算等于 1024 就永远开不了思考。
    /// 32000 是 Claude 各版本都接受的量级，也给思考留得下 16k 以上的空间。
    /// 用户在模型配置里填了 <c>MaxTokens</c> 就用用户的。
    /// </summary>
    private const int DefaultAnthropicMaxTokens = 32000;

    /// <summary>
    /// Anthropic 线的思考块归一层：发出去之前把「正文」与「签名」合成一条。
    /// 只做这一件事，其余原样透传。
    /// </summary>
    private sealed class AnthropicThinkingChatClient : DelegatingChatClient
    {
        public AnthropicThinkingChatClient(IChatClient innerClient) : base(innerClient)
        {
        }

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(AnthropicThinkingNormalizer.Normalize(messages), options, cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(AnthropicThinkingNormalizer.Normalize(messages), options, cancellationToken);
    }
}
