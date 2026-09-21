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

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 一个工具定义的估算构成
/// </summary>
/// <param name="Name">工具名</param>
/// <param name="Description">描述</param>
/// <param name="Schema">参数 schema（已压掉排版）</param>
public readonly record struct ToolTokenBreakdown(int Name, int Description, int Schema)
{
    /// 每个工具在请求体里还有一层固定封装(类型标记、括号、分隔),按经验值补一笔
    private const int Envelope = 8;

    /// <summary>合计</summary>
    public int Total => Name + Description + Schema + Envelope;
}

/// <summary>
/// 工具定义的 token 占用估算。
///
/// 存在的理由是<b>可见性</b>：工具定义随每一轮请求完整重发，用不用都付钱，
/// 而本地模型的窗口常常只有几 K——接两个 server 就可能吃掉一大半。
/// 用户看不见这笔账，就只会觉得"模型变笨了"。
///
/// 分词一律走 <see cref="LlmTokenizer"/>，与输入框那个字数统计<b>同一把尺子</b>：
/// 同一段文本在两处显示不同的数字，比不显示更糟。
/// </summary>
public static class ToolTokenEstimator
{
    /// <summary>
    /// 估算一段文本的 token 数
    /// </summary>
    /// <param name="text">文本</param>
    /// <returns>估算值</returns>
    public static int EstimateText(string? text)
    {
        return string.IsNullOrEmpty(text) ? 0 : LlmTokenizer.CountTokens(text);
    }

    /// <summary>
    /// 估算一个工具定义的 token 数(名字 + 描述 + 参数 schema)
    /// </summary>
    /// <param name="tool">工具</param>
    /// <returns>估算值</returns>
    public static int Estimate(AITool tool)
    {
        return Breakdown(tool).Total;
    }

    /// <summary>
    /// 按构成拆开估算一个工具定义。
    ///
    /// 分项存在的理由是<b>可归因</b>：估算与服务端实报对不上时，「贵在哪一段」是唯一能往下查的线索——
    /// 描述特别长、schema 本身就大、还是分词器在 JSON 上吃亏，三者的处置完全不同。
    /// </summary>
    /// <param name="tool">工具</param>
    /// <returns>分项估算</returns>
    public static ToolTokenBreakdown Breakdown(AITool tool)
    {
        return new ToolTokenBreakdown(
            EstimateText(tool.Name),
            EstimateText(tool.Description),
            tool is AIFunctionDeclaration function ? EstimateSchema(function.JsonSchema) : 0);
    }

    /// <summary>
    /// 估算一份参数 schema 的 token 数。
    ///
    /// <b>先压掉排版再分词。</b><see cref="JsonElement.GetRawText"/> 还给你的是解析<b>之前</b>
    /// 那份原文——server 若回的是缩进过的 JSON，每一层缩进与换行都会被当成 token 计进去，
    /// 而请求体里发出去的是压缩形态。实测一个 36 工具的 MCP server 因此虚高三倍多
    /// （估 21k、实际约 6.5k），足以让用户去关一个其实并不贵的 server。
    /// </summary>
    /// <param name="schema">参数 schema</param>
    /// <returns>估算值</returns>
    public static int EstimateSchema(JsonElement schema)
    {
        return EstimateText(CompactSchema(schema));
    }

    /// <summary>
    /// 把 schema 还原成请求体里那种压缩形态的文本。
    /// 诊断时也用它：字符数与 token 数一并看，才分得清「原文就大」还是「分词吃亏」。
    /// </summary>
    /// <param name="schema">参数 schema</param>
    /// <returns>压缩后的 JSON 文本</returns>
    public static string CompactSchema(JsonElement schema)
    {
        // 走 Utf8JsonWriter 而不是 JsonSerializer:默认就是压缩形态,且不牵扯反射序列化
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            schema.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 估算一组工具定义的 token 总数
    /// </summary>
    /// <param name="tools">工具集</param>
    /// <returns>估算值</returns>
    public static int Estimate(IEnumerable<AITool> tools)
    {
        int total = 0;
        foreach (AITool tool in tools) total += Estimate(tool);
        return total;
    }
}
