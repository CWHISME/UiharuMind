/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 历史的 JSONL 编解码:一行一条 ChatMessage(仍用 SessionJsonOptions 保持 AIContent 多态)。
/// 追加式写入让每轮落盘成本与会话长度无关;进程中断最坏留下一个残缺尾行,
/// 解析端逐行容错跳过,已有历史不受影响。
/// </summary>
public static class HistoryJsonl
{
    /// <summary>单行序列化配置:JSONL 一行一条,不能用缩进版 SessionJsonOptions</summary>
    private static readonly JsonSerializerOptions LineOptions = CreateLineOptions();

    private static JsonSerializerOptions CreateLineOptions()
    {
        JsonSerializerOptions options = new(SessionJsonOptions.Default) { WriteIndented = false };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// 序列化一批消息为若干行(每行以换行符结尾)
    /// </summary>
    /// <param name="messages">消息</param>
    /// <returns>JSONL 文本</returns>
    public static string SerializeLines(IEnumerable<ChatMessage> messages)
    {
        StringBuilder sb = new();
        foreach (ChatMessage message in messages)
        {
            sb.Append(JsonSerializer.Serialize(message, LineOptions)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 逐行解析,残缺/损坏行跳过并告警
    /// </summary>
    /// <param name="lines">文本行</param>
    /// <returns>解析成功的消息</returns>
    public static List<ChatMessage> Parse(IEnumerable<string> lines)
    {
        List<ChatMessage> result = new();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                ChatMessage? message = JsonSerializer.Deserialize<ChatMessage>(line, LineOptions);
                if (message != null) result.Add(message);
            }
            catch (JsonException e)
            {
                Log.Warning($"Skip malformed history line: {e.Message}");
            }
        }

        return result;
    }
}
