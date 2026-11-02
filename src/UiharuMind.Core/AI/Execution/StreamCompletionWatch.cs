using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 模型把正文退化成一长串空白（说完一句话后连续吐几千个换行），这一发没有任何工具调用。
/// 与复读不同，换一次采样多半就正常，所以交给断线重试而不是直接失败。
/// </summary>
/// <param name="trailingWhitespace">正文尾部连续空白的长度</param>
public sealed class DegeneratedReplyException(int trailingWhitespace)
    : Exception($"模型回复退化成空白（尾部连续 {trailingWhitespace} 个空白字符，且没有工具调用）")
{
    /// <summary>
    /// 正文尾部连续空白的长度
    /// </summary>
    public int TrailingWhitespace { get; } = trailingWhitespace;
}

/// <summary>
/// 判定一次流式回复是否被半路截断：流正常收尾（带 [DONE]、不抛异常），却既没有 finish_reason、
/// 也没有正文或工具调用。中转网关断了上游时会自己补上用量帧和 [DONE]，传输层看不出异常，
/// 只能从内容上认（实测 agnes 一轮只思考了 1.8 万字就收尾，finish_reason 缺失）。
/// 只有思考、但带了 finish_reason 的不算——那是服务端明确说完了。
/// </summary>
internal sealed class StreamCompletionWatch
{
    /// <summary>
    /// 正文尾部连续空白超过这么长就算退化刷屏。实测 ling-3.1-flash 一轮吐了 2948 个换行才 stop，
    /// 而 <see cref="RepetitionLoopDetector"/> 要 4096 字的窗口才判，这类两千多字的漏得过去
    /// </summary>
    public const int WhitespaceFloodChars = 400;

    private bool _sawAnswer; //见过正文或工具调用
    private bool _sawToolCall; //见过工具调用
    private int _trailingWhitespace; //正文尾部连续空白的长度

    /// <summary>
    /// 最后见到的 finish_reason；没见过为 null
    /// </summary>
    public string? FinishReason { get; private set; }

    /// <summary>
    /// 流已收尾但看起来被截断了
    /// </summary>
    public bool IsCutOff => FinishReason == null && !_sawAnswer;

    /// <summary>
    /// 正文尾部堆了一大段空白、且这一发没有工具调用：回合会就此停住，该重发
    /// </summary>
    public bool IsWhitespaceFlooded => !_sawToolCall && _trailingWhitespace >= WhitespaceFloodChars;

    /// <summary>
    /// 正文尾部连续空白的长度
    /// </summary>
    public int TrailingWhitespace => _trailingWhitespace;

    /// <summary>
    /// 观察一条流式增量
    /// </summary>
    /// <param name="update">流式增量</param>
    public void Observe(ChatResponseUpdate update)
    {
        if (update.FinishReason is { } reason) FinishReason = reason.ToString();

        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent:
                    _sawAnswer = true;
                    _sawToolCall = true;
                    break;
                case TextContent { Text.Length: > 0 } text:
                    _sawAnswer = true;
                    TrackTrailingWhitespace(text.Text);
                    break;
            }
        }
    }

    // 只看尾部那段空白：整段是空白就往上累加，否则从最后一个非空白字符重新算
    private void TrackTrailingWhitespace(string text)
    {
        int i = text.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
        _trailingWhitespace = i < 0 ? _trailingWhitespace + text.Length : text.Length - 1 - i;
    }
}
