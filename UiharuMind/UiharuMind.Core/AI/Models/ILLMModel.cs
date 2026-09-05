/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.Models;

public interface ILlmModel
{
    string ModelName { get; }
    string ModelPath { get; }
    bool IsVision { get; }
    string ModelDescription { get; }
    string ModelId { get; }
    int Port { get; }

    /// <summary>
    /// 追加到请求体的额外参数。按模型配置的思考力度产出,一档可对应多个参数
    /// (如同时给出 thinking 与 reasoning_effort,各后端自行取舍)。
    /// </summary>
    /// <returns>要写入请求 JSON 的键值对;无需追加时为空</returns>
    public IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams() => null;

    /// <summary>
    /// 思考模式下,助手消息带 tool_calls 时是否要求原样带回当时的 reasoning_content。
    /// 目前只有 DeepSeek 已确认有此强制要求;其余共用 thinking/reasoning_effort 参数的
    /// 兼容服务未必有此限制,默认不开,避免给不需要的后端塞进多余字段。
    /// </summary>
    public bool RequiresReasoningContentRoundtrip => false;
}