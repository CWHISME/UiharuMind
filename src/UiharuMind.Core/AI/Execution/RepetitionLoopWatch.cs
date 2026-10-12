using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 模型输出陷入复读，本轮已中止。不走断线重试：同样的上下文重发多半还会复读，白烧时间和 token
/// </summary>
public sealed class RepetitionLoopException(string unit)
    : Exception($"模型输出陷入复读（反复生成「{Preview(unit)}」），已自动中止本轮。")
{
    private static string Preview(string unit)
    {
        string flat = unit.Replace('\n', ' ').Replace('\r', ' ');
        return flat.Length <= 40 ? flat : flat[..40] + "…";
    }
}

/// <summary>
/// 盯一次流式回复的思考与正文两路，任一路复读就抛 <see cref="RepetitionLoopException"/>
/// </summary>
internal sealed class RepetitionLoopWatch
{
    private readonly RepetitionLoopDetector _reasoning = new();
    private readonly RepetitionLoopDetector _text = new();

    /// <summary>
    /// 观察一条流式增量
    /// </summary>
    /// <param name="update">流式增量</param>
    /// <exception cref="RepetitionLoopException">认出复读</exception>
    public void Observe(ChatResponseUpdate update)
    {
        foreach (AIContent content in update.Contents)
        {
            string? unit = content switch
            {
                TextReasoningContent { Text.Length: > 0 } reasoning => _reasoning.Feed(reasoning.Text),
                TextContent { Text.Length: > 0 } text => _text.Feed(text.Text),
                _ => null,
            };
            if (unit != null) throw new RepetitionLoopException(unit);
        }
    }
}
