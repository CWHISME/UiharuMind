using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 模型输出陷入复读，本轮已中止。不走断线重试：同样的上下文重发多半还会复读，白烧时间和 token
/// </summary>
public sealed class RepetitionLoopException(string channel, string unit, string? modelName = null)
    : Exception($"{(modelName == null ? "模型" : $"模型 {modelName}")}输出陷入复读（{channel}反复生成「{Flatten(unit)}」×约{RepetitionLoopDetector.WindowChars / unit.Length}遍），已自动中止本轮。")
{
    // 单元最长 256 字（见 RepetitionLoopDetector.MaxPeriod），全打出来也不长；
    // 只压平换行不截断：上次只留 40 字，查日志根本看不出在复读什么
    private static string Flatten(string unit) => unit.Replace('\n', ' ').Replace('\r', ' ');
}

/// <summary>
/// 盯一次流式回复的思考与正文两路，任一路复读就抛 <see cref="RepetitionLoopException"/>
/// </summary>
internal sealed class RepetitionLoopWatch(string? modelName = null)
{
    private readonly RepetitionLoopDetector _reasoning = new();
    private readonly RepetitionLoopDetector _text = new();
    private readonly string? _modelName = modelName;

    /// <summary>
    /// 观察一条流式增量
    /// </summary>
    /// <param name="update">流式增量</param>
    /// <exception cref="RepetitionLoopException">认出复读</exception>
    public void Observe(ChatResponseUpdate update)
    {
        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case TextReasoningContent { Text.Length: > 0 } reasoning:
                    ThrowIfLooping(_reasoning, reasoning.Text, "思考");
                    break;
                case TextContent { Text.Length: > 0 } text:
                    ThrowIfLooping(_text, text.Text, "正文");
                    break;
            }
        }
    }

    private void ThrowIfLooping(RepetitionLoopDetector detector, string delta, string channel)
    {
        string? unit = detector.Feed(delta);
        if (unit != null) throw new RepetitionLoopException(channel, unit, _modelName);
    }
}
