/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 从文本流中恢复漏成纯文本的工具调用。写法按 <see cref="TextToolCallDialects.All"/> 分派——
/// GLM 线的 <c>&lt;arg_key&gt;K&lt;/arg_key&gt;&lt;arg_value&gt;V&lt;/arg_value&gt;</c>，
/// MiMo 线的 <c>&lt;function=名字&gt;&lt;parameter=K&gt;V</c>，两种都认。
///
/// 这些模型以 XML 作为工具调用协议,本应由服务端解析成结构化 tool_calls;
/// 思考模式下模型把调用写进思考通道、或服务端只解析出一半（MiMo 会给一条 arguments 为空的调用）
/// 时,它会以纯文本漏进响应——框架的函数调用循环只认结构化 <see cref="FunctionCallContent"/>,
/// 于是"模型以为调了工具,管线以为它说完了",一轮就此终止。
/// 本解析器把完整的调用块转回结构化内容,其余文本原样放行。
///
/// 两道安全闸:仅在请求带工具时启用(由调用方决定);解析出的工具名必须命中实际装配的工具,
/// 否则整块按原文放行——讨论这种语法的正常文本不受影响。跨增量切断安全(块可任意分片到达)。
/// </summary>
internal sealed class TextToolCallStreamParser
{
    private const string OpenTag = "<tool_call>";
    private const string CloseTag = "</tool_call>";
    private const int MaxCaptureChars = 16_384; //捕获中的块超过此长度仍无闭合标签,按原文放弃

    private readonly IReadOnlySet<string> _toolNames;
    private readonly StringBuilder _buffer = new(); //尚未定性的文本(可能含未闭合的调用块)
    private int _callSequence;

    public TextToolCallStreamParser(IReadOnlySet<string> toolNames)
    {
        _toolNames = toolNames;
    }

    /// <summary>
    /// 喂入一段增量,返回可放行的文本与解析出的调用
    /// </summary>
    /// <param name="delta">文本增量</param>
    /// <returns>净化后的文本(可为空串)与完整解析出的调用列表</returns>
    public (string Text, List<FunctionCallContent> Calls) Feed(string delta)
    {
        _buffer.Append(delta);
        return Drain(final: false);
    }

    /// <summary>
    /// 流结束:清空缓冲。未闭合的调用块按原文放行,绝不吞内容。
    /// </summary>
    /// <returns>剩余文本与调用</returns>
    public (string Text, List<FunctionCallContent> Calls) Flush()
    {
        return Drain(final: true);
    }

    private (string Text, List<FunctionCallContent> Calls) Drain(bool final)
    {
        StringBuilder text = new();
        List<FunctionCallContent> calls = [];

        while (true)
        {
            string pending = _buffer.ToString();
            int open = pending.IndexOf(OpenTag, StringComparison.Ordinal);
            if (open < 0)
            {
                // 无起始标签:除可能是起始标签前缀的尾巴外全部放行
                int hold = final ? 0 : PartialPrefixSuffixLength(pending);
                text.Append(pending, 0, pending.Length - hold);
                _buffer.Clear();
                if (hold > 0) _buffer.Append(pending, pending.Length - hold, hold);
                break;
            }

            text.Append(pending, 0, open);
            int close = pending.IndexOf(CloseTag, open + OpenTag.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                // 块未闭合:继续等待;流已结束或超限则按原文放弃捕获
                if (final || pending.Length - open > MaxCaptureChars)
                {
                    text.Append(pending, open, pending.Length - open);
                    _buffer.Clear();
                    break;
                }

                _buffer.Clear();
                _buffer.Append(pending, open, pending.Length - open);
                break;
            }

            string block = pending.Substring(open + OpenTag.Length, close - open - OpenTag.Length);
            if (TryParseCall(block, out FunctionCallContent? call))
            {
                calls.Add(call!);
            }
            else
            {
                // 名字不命中已装配工具或形状不对:按原文放行,不做聪明事
                text.Append(pending, open, close + CloseTag.Length - open);
            }

            string rest = pending[(close + CloseTag.Length)..];
            _buffer.Clear();
            _buffer.Append(rest);
        }

        return (text.ToString(), calls);
    }

    private bool TryParseCall(string block, out FunctionCallContent? call)
    {
        call = null;
        foreach (ITextToolCallDialect dialect in TextToolCallDialects.All)
        {
            if (!dialect.TryParse(block, out string name, out Dictionary<string, object?> arguments)) continue;
            // 名字不命中已装配工具或形状不对:按原文放行,不做聪明事
            if (!_toolNames.Contains(name)) continue;

            call = new FunctionCallContent($"text_tool_call_{++_callSequence}", name, arguments);
            return true;
        }

        return false;
    }

    /// <summary>尾部若是起始标签的前缀(如 "&lt;tool_ca"),返回该前缀长度以便扣留</summary>
    private static int PartialPrefixSuffixLength(string pending)
    {
        int max = Math.Min(OpenTag.Length - 1, pending.Length);
        for (int len = max; len > 0; len--)
        {
            if (pending.AsSpan(pending.Length - len).SequenceEqual(OpenTag.AsSpan(0, len))) return len;
        }

        return 0;
    }
}
