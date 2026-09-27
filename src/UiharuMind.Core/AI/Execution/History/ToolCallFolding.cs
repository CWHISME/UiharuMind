using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 工具调用组折叠后的样子（ADR 0006 修订）。
///
/// 框架默认的格式（<c>ToolResultCompactionStrategy.DefaultToolCallFormatter</c>）把结果<b>原文照抄</b>，
/// 只去掉调用参数与结构——读文件、搜代码这类大结果折了等于没折，腾地方的活全落到截断头上。
/// 这里留下工具名与参数（读的是哪个文件、搜的是什么，比结果本身更难重建），结果只留开头一截并注明原长；
/// 要原文，模型重新调用一次即可。输出只取决于那一组消息，同一段历史每次折出来逐字相同，前缀缓存稳定
/// </summary>
internal static class ToolCallFolding
{
    private const int ResultPreviewChars = 200; //结果留多长的开头
    private const int ArgumentsChars = 160; //参数超过这么长就截断

    /// <summary>折叠摘要的首行：告诉模型原文已不在上下文里</summary>
    public const string Header = "[已折叠的工具调用：结果只留开头，需要原文就重新调用]";

    /// <summary>
    /// 把一组工具调用折成一段摘要
    /// </summary>
    /// <param name="group">工具调用组（助手的调用 + 对应的工具结果）</param>
    /// <returns>摘要正文</returns>
    public static string Format(CompactionMessageGroup group)
    {
        Dictionary<string, string> results = new();
        List<FunctionCallContent> calls = [];
        foreach (AIContent content in group.Messages.SelectMany(x => x.Contents))
        {
            if (content is FunctionCallContent call) calls.Add(call);
            else if (content is FunctionResultContent { CallId: { } callId } result)
                results[callId] = result.Result?.ToString() ?? string.Empty;
        }

        StringBuilder text = new(Header);
        foreach (FunctionCallContent call in calls)
        {
            text.Append("\n- ").Append(call.Name);
            if (call.Arguments is { Count: > 0 } arguments)
                text.Append(' ').Append(Clip(JsonSerializer.Serialize(arguments), ArgumentsChars));
            if (results.TryGetValue(call.CallId, out string? result)) text.Append("：").Append(Preview(result));
        }

        return text.ToString();
    }

    private static string Preview(string result)
    {
        string value = result.Trim();
        if (value.Length <= ResultPreviewChars) return value;
        return $"{value[..ResultPreviewChars]}……（共 {value.Length} 字）";
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
