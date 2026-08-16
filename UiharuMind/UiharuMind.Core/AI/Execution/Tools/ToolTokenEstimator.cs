/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools;

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
        int tokens = EstimateText(tool.Name) + EstimateText(tool.Description);
        if (tool is AIFunctionDeclaration function)
        {
            tokens += EstimateText(function.JsonSchema.GetRawText());
        }

        // 每个工具在请求体里还有一层固定封装(类型标记、括号、分隔),按经验值补一笔
        return tokens + 8;
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
