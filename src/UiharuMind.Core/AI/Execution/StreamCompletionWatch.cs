using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 判定一次流式回复是否被半路截断：流正常收尾（带 [DONE]、不抛异常），却既没有 finish_reason、
/// 也没有正文或工具调用。中转网关断了上游时会自己补上用量帧和 [DONE]，传输层看不出异常，
/// 只能从内容上认（实测 agnes 一轮只思考了 1.8 万字就收尾，finish_reason 缺失）。
/// 只有思考、但带了 finish_reason 的不算——那是服务端明确说完了。
/// </summary>
internal sealed class StreamCompletionWatch
{
    private bool _sawAnswer; //见过正文或工具调用

    /// <summary>
    /// 最后见到的 finish_reason；没见过为 null
    /// </summary>
    public string? FinishReason { get; private set; }

    /// <summary>
    /// 流已收尾但看起来被截断了
    /// </summary>
    public bool IsCutOff => FinishReason == null && !_sawAnswer;

    /// <summary>
    /// 观察一条流式增量
    /// </summary>
    /// <param name="update">流式增量</param>
    public void Observe(ChatResponseUpdate update)
    {
        if (update.FinishReason is { } reason) FinishReason = reason.ToString();
        if (_sawAnswer) return;
        foreach (AIContent content in update.Contents)
        {
            if (content is FunctionCallContent or TextContent { Text.Length: > 0 })
            {
                _sawAnswer = true;
                return;
            }
        }
    }
}
