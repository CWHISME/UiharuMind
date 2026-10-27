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
    /// 思考模式下,助手消息带 tool_calls 时是否要求原样带回当时的思考正文。
    /// <c>null</c>=自动(历史有思考就回传);<c>true</c>/<c>false</c>=显式要求/禁止。
    /// 本地模型没有思考回传一说,默认不回传。
    /// </summary>
    public bool? RequiresReasoningContentRoundtrip => false;

    /// <summary>
    /// 回填思考正文用的 wire 键名(reasoning_content/reasoning/reasoning_text)。
    /// 请求侧不写死键名,按模型实际值回填;本地模型取默认。
    /// </summary>
    public string ThinkingField => "reasoning_content";

    /// <summary>
    /// 是否不发送采样参数(temperature/top_p/presence_penalty/frequency_penalty)。
    /// 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,默认发送,按模型单独开启。
    /// </summary>
    public bool OmitSamplingParams => false;
}