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
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.AI.Net;

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
    /// <returns>聊天客户端</returns>
    public static IChatClient Create(OpenAICompatibleHttpHandler handler, ILlmModel? model, string modelId, string apiKey,
        ClientRetryPolicy? retryPolicy = null)
    {
        // HttpClient.Timeout 默认就是 100s(管到响应头/首字节),SDK 自带客户端反而设成了 Infinite。
        // 流式长思考会撞它,一并关掉,裁决交给上层 CancellationToken
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
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

        return new ChatClient(modelId, new ApiKeyCredential(apiKey), options).AsIChatClient();
    }
}
