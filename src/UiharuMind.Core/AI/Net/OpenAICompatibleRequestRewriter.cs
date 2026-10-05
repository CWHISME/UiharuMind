/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 发往 OpenAI 兼容端点之前对请求体的几道改写：注入模型的额外参数、禁止工具调用、
/// 修畸形的工具参数、回填思考正文、删采样参数。
/// </summary>
internal static class OpenAICompatibleRequestRewriter
{
    /// <summary>
    /// 开启 <c>OmitSamplingParams</c> 的模型要删掉的采样参数。只删这四个,
    /// <c>max_tokens/thinking/tool_choice</c> 等不受影响。
    /// </summary>
    internal static readonly string[] SamplingParamKeys =
        ["temperature", "top_p", "presence_penalty", "frequency_penalty"];

    // 宽松编码器与 SDK 自己序列化请求体的口径一致：默认编码器会把中文全写成 \uXXXX，
    // 每次改写后发出去的请求体凭空大三成（中文为主的长会话实测 458KB → 595KB）
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 按模型配置与本次请求上下文（<see cref="LlmRequestContext"/>）改写请求体
    /// </summary>
    /// <param name="json">SDK 序列化出的请求体</param>
    /// <param name="model">目标模型；为 null 时只做与模型无关的改写</param>
    /// <returns>改写后的请求体；无需改写或不是 JSON 对象时原样返回</returns>
    public static string Rewrite(string json, ILlmModel? model)
    {
        var extraParams = model?.GetExtraParams();
        bool forbidToolCalls = LlmRequestContext.ForbidToolCalls;
        // 只对已确认要求 reasoning_content 回填的模型(目前只有 DeepSeek)生效,
        // 其余共用 thinking/reasoning_effort 参数的兼容服务不无谓塞多余字段
        var reasoningByCallId = model?.RequiresReasoningContentRoundtrip == true
            ? LlmRequestContext.PendingReasoningByCallId
            : null;
        // 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,开启后删掉这四个字段,不碰其它参数
        bool omitSamplingParams = model?.OmitSamplingParams == true;

        // 大多数请求不含畸形 tool_calls 参数,先做一次廉价子串扫描,避免每次都解析 JSON
        bool needsArgFix = json.Contains("\"arguments\":\"null\"") || json.Contains("\"arguments\": \"null\"");

        if (extraParams is not { Count: > 0 } && !forbidToolCalls && !needsArgFix &&
            reasoningByCallId is not { Count: > 0 } && !omitSamplingParams)
        {
            return json;
        }

        if (JsonNode.Parse(json)?.AsObject() is not { } jsonNode) return json;

        if (omitSamplingParams) StripSamplingParams(jsonNode);

        if (extraParams != null)
        {
            foreach (var extraParam in extraParams)
            {
                jsonNode[extraParam.Key] = extraParam.Value;
            }
        }

        // 带着工具定义(前缀缓存要对齐)但不许调用。MEAI 的 ChatToolMode 没有 None,
        // 只能在这一层直接写进请求体
        if (forbidToolCalls) jsonNode["tool_choice"] = "none";

        if (needsArgFix) SanitizeMalformedToolCallArguments(jsonNode);

        if (reasoningByCallId is { Count: > 0 }) RestoreReasoningContent(jsonNode, reasoningByCallId);

        return jsonNode.ToJsonString(CompactJson);
    }

    /// <summary>
    /// 删掉请求体里的采样参数,供采样参数固定的模型(如 Kimi)使用——
    /// 这类服务端对显式传值直接 400,只能不发,让服务端用默认值。
    /// </summary>
    /// <param name="jsonNode">请求体根对象</param>
    internal static void StripSamplingParams(JsonObject jsonNode)
    {
        foreach (string key in SamplingParamKeys)
            jsonNode.Remove(key);
    }

    /// <summary>
    /// 思考模式下,助手消息带 tool_calls 时接口要求原样带回当时的 reasoning_content,
    /// 否则报 "If thinking mode and tool_calls, reasoning_content must be passed back to the API"。
    /// 标准 ChatMessage→wire 消息转换认不出 <c>TextReasoningContent</c>,序列化时会把它悄悄丢掉,
    /// 只能按 tool_call id 从 <see cref="LlmRequestContext.PendingReasoningByCallId"/> 找回来补上。
    /// 一条消息可带多个 tool_calls,任一 id 命中即恢复整条消息的思考正文。
    /// </summary>
    /// <param name="jsonNode">请求体根对象</param>
    /// <param name="reasoningByCallId">本次请求历史里,按 tool_call id 索引的思考正文</param>
    internal static void RestoreReasoningContent(JsonObject jsonNode,
        IReadOnlyDictionary<string, string> reasoningByCallId)
    {
        if (jsonNode["messages"] is not JsonArray messages) return;

        foreach (var message in messages)
        {
            if (message is not JsonObject messageObj) continue;
            if (messageObj["reasoning_content"] != null) continue; // 已经带了,不覆盖
            if (messageObj["tool_calls"] is not JsonArray { Count: > 0 } toolCalls) continue;

            string? reasoningText = null;
            foreach (var toolCall in toolCalls)
            {
                if (toolCall is not JsonObject call) continue;
                if (call["id"] is JsonValue idValue &&
                    idValue.TryGetValue(out string? callId) &&
                    reasoningByCallId.TryGetValue(callId, out string? text))
                {
                    reasoningText = text;
                    break;
                }
            }

            if (reasoningText != null) messageObj["reasoning_content"] = reasoningText;
        }
    }

    /// <summary>
    /// 模型偶发地把无参调用的 arguments 序列化成字面字符串 "null"(而非空对象 "{}")。
    /// 部分 OpenAI 兼容后端会对历史里的 arguments 做 json.loads 后 .items(),
    /// 解析出 None 就直接 400——'NoneType' object has no attribute 'items'。
    /// 修的是发出去的历史,不影响这次调用本身的执行结果。
    /// </summary>
    /// <param name="jsonNode">请求体根对象</param>
    private static void SanitizeMalformedToolCallArguments(JsonObject jsonNode)
    {
        if (jsonNode["messages"] is not JsonArray messages) return;

        foreach (var message in messages)
        {
            if (message?["tool_calls"] is not JsonArray toolCalls) continue;

            foreach (var toolCall in toolCalls)
            {
                if (toolCall?["function"] is not JsonObject function) continue;
                if (function["arguments"] is JsonValue value &&
                    value.TryGetValue(out string? arguments) && arguments == "null")
                {
                    function["arguments"] = "{}";
                }
            }
        }
    }
}
