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
    string ApiKey { get; }

    /// <summary>
    /// 追加到请求体的额外参数。按本次请求的思考力度产出,一档可对应多个参数
    /// (如同时给出 thinking 与 reasoning_effort,各后端自行取舍)。
    /// </summary>
    /// <param name="thinkingMode">本次请求的思考力度</param>
    /// <returns>要写入请求 JSON 的键值对;无需追加时为空</returns>
    public IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams(EThinkingMode thinkingMode) => null;
}