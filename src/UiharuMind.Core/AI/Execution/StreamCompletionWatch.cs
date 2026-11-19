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
/// 也没有正文、工具调用或<b>有效思考</b>。中转网关断了上游时会自己补上用量帧和 [DONE]，
/// 传输层看不出异常（<c>SseSanitizingStream</c> 在 EOF 无 [DONE] 时也只打日志哨兵、不抛异常），
/// 只能从内容上认——这正是判据存在的理由，而不是因为「断流都会抛异常」：那条缝仍然存在。
///
/// <para><b>思考也是产出</b>：实测 agnes 一轮只思考了 1.8 万字就收尾、finish_reason 缺失——
/// 把只有思考的一发当作「无产出断流」整段作废（StreamRestartContent），会丢掉思考者真实
/// 想过的内容，还要重烧一遍 token。所以非空、非全空白的思考算产出；空白思考不算
/// （与 <c>LazyChatClient</c> 统计口径一致；落盘侧 normalizer 保留空白，那是保存的口径、
/// 与断流判定无关，是有意的不对称）。</para>
///
/// <para><b>加密思考（密文）另说</b>：chat 线的票据在适配层就被丢弃、到不了这里；
/// responses/messages 线以 <see cref="TextReasoningContent.ProtectedData"/> 出现时 Text 为空。
/// 密文是整块一帧语义（<c>ReasoningDetailExtractor</c> 要求同一行内完整闭合，残缺直接放弃），
/// 客户端不解密、只回传服务端在下一轮解密——所以<b>半截密文不会入库；只有完整票 + 没回答的
/// 轮次</b>才会命中这里。那种轮次没有答案，票不可见，重发拿完整轮次比保留一张票更值，
/// 因此密文<b>不算产出</b>，仍是 IsCutOff（重发）。</para>
/// </summary>
internal sealed class StreamCompletionWatch
{
    /// <summary>
    /// 正文尾部连续空白超过这么长就算退化刷屏。实测 ling-3.1-flash 一轮吐了 2948 个换行才 stop，
    /// 而 <see cref="RepetitionLoopDetector"/> 要 4096 字的窗口才判，这类两千多字的漏得过去
    /// </summary>
    public const int WhitespaceFloodChars = 400;

    private bool _sawAnswer; //见过正文、有效思考或工具调用
    private bool _sawToolCall; //见过工具调用
    private bool _transportReported; //传输层是否报告过收尾（EOF 已到）
    private bool _transportSawDone; //传输层报告时是否见过 [DONE]
    private int _trailingWhitespace; //正文尾部连续空白的长度

    /// <summary>
    /// 最后见到的 finish_reason；没见过为 null
    /// </summary>
    public string? FinishReason { get; private set; }

    /// <summary>
    /// 流已收尾但看起来被截断了。
    ///
    /// 两个输入合成：内容侧（有没有产出）与传输侧（收尾状态）。
    /// 未报告传输侧时只认内容——有正文/有效思考/工具调用就不算断流（思考也是产出）；
    /// 报告且<b>未见 [DONE]</b>（服务端掐了连接）时，只要没有 finish_reason 一律算真断流——
    /// 内容被截断到一半同样是断流，正文/思考/工具调用的增量都救不回来
    /// </summary>
    public bool IsCutOff => FinishReason == null && (!_sawAnswer || (_transportReported && !_transportSawDone));

    /// <summary>
    /// 传输层报告整条流的收尾状态。<paramref name="sawDone"/> 为 true 表示读过 [DONE]（正常收尾），
    /// false 表示 EOF 时没见过 [DONE]（服务端掐了连接）。
    ///
    /// 不调用（默认）等于「未报告」，判据只按内容走：responses/messages 线没有 [DONE] 哨兵、
    /// 本地模型与不经字节层的 fake 也不报告，它们的行为不能因此翻转。
    /// </summary>
    /// <param name="sawDone">是否见过 [DONE]</param>
    public void MarkTransportEnd(bool sawDone)
    {
        _transportReported = true;
        _transportSawDone = sawDone;
    }

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
                // 有效思考也是产出：模型真的想过（agnes 一轮 1.8 万字）。不认它的话，
                // 思考段假收尾会被当作「无产出断流」整段重发，第一发的思考全部作废。
                // 只看非空且非全空白：孤立空格不算（实测 deepseek 会发），与统计口径同款；
                // 加密思考（Text 为空、内容在 ProtectedData）不算，见类注释
                case TextReasoningContent { Text.Length: > 0 } reasoning
                    when !string.IsNullOrWhiteSpace(reasoning.Text):
                    _sawAnswer = true;
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
