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
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.AI.Net.Request;

namespace UiharuMind.Core.AI.Net;

// SDK 把 responses 线标为实验性 API（OPENAI001）。这条线是 ADR 0100 定的增量路，
// 用它是为了让加密思考票据由 SDK 原样往返（实测 ProtectedData → encrypted_content 逐字节不变）。
#pragma warning disable OPENAI001

/// <summary>
/// 走 OpenAI 兼容端点的聊天客户端（本地 llama.cpp 服务与远程模型共用）
/// </summary>
internal static class OpenAICompatibleChatClient
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
    public static IChatClient Create(OpenAICompatibleHttpHandler handler, ILlmModel? model, string modelId, string apiKey,
        ClientRetryPolicy? retryPolicy = null, SendGate? sendGate = null)
    {
        // HttpClient.Timeout 默认就是 100s(管到响应头/首字节),SDK 自带客户端反而设成了 Infinite。
        // 流式长思考会撞它,一并关掉,裁决交给上层 CancellationToken
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        // responses 线走另一个客户端：请求体形状（input item 数组）与响应形状（SSE 事件）都不同，
        // 改写器与解析器整套换掉。SDK 已经把这两件事做完了（ToOpenAIResponseItems / 事件翻译），
        // 自己手写一遍只会多一处要维护的协议实现。
        if (model?.ApiFormat == EApiFormat.Responses)
            return CreateResponsesClient(httpClient, modelId, apiKey, retryPolicy, sendGate);

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            // 流式响应的读闸默认 100s:思考期 chunk 间隔一长就被 ReadTimeoutStream 掐断,
            // 且那异常在 HTTP 200 之后冒出,OpenAICompatibleHttpHandler 看不到,只会被上层当用户取消吞掉。
            // 读卡的裁决完全交给上层 CancellationToken(用户停止按钮),这里不设静态时限
            NetworkTimeout = Timeout.InfiniteTimeSpan,
        };
        if (retryPolicy != null) options.RetryPolicy = retryPolicy;
        options.AddPolicy(new OpenAICompatibleRequestPolicy(model), PipelinePosition.PerCall);
        if (sendGate != null) options.AddPolicy(new SendGatePolicy(sendGate), PipelinePosition.PerTry);

        return new ChatClient(modelId, new ApiKeyCredential(apiKey), options).AsIChatClient();
    }

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
}
