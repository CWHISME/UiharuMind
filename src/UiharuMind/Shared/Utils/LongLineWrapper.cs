/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Text;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 超长单行的硬切规则。
///
/// 为什么需要：AvaloniaEdit 的虚拟化是<b>按行</b>的——一行 480KB 的 minified JSON 在它眼里
/// 仍然只是一个 <c>VisualLine</c>，滚到哪儿都要把整行测量一遍，虚拟化救不了。
/// 把它硬切成 480 行之后，虚拟化才真正生效：屏幕外的行一行都不用量。
///
/// 与工具结果卡片的截断规则（<c>ToolResultTruncation</c>）刻意分开：那边关掉的是
/// <c>TextWrapping=Wrap</c> 的整篇重排，成本正比于<b>字符总数</b>；这边成本正比于<b>单行长度</b>。
/// 合并成一套常数只会让两个不相干的成本互相绑架。
///
/// 做成脱离控件的纯函数，因此可以在不初始化 Avalonia 的前提下被测到。
/// </summary>
public static class LongLineWrapper
{
    /// <summary>单行字符上限</summary>
    public const int MaxLineChars = 1000;

    /// <summary>
    /// 是否存在超过上限的行
    /// </summary>
    /// <param name="text">待检查的文本，可空</param>
    /// <returns>存在超长行返回 true；null 或空串返回 false</returns>
    public static bool NeedsWrap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        int lineStart = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;
            if (ContentEnd(text, lineStart, lineEnd) - lineStart > MaxLineChars) return true;
            if (newline < 0) return false;
            lineStart = newline + 1;
        }
    }

    /// <summary>
    /// 把超过上限的行硬切成多行（插入 <c>'\n'</c>）。
    ///
    /// 没有超长行时<b>原样返回同一引用</b>（<c>ReferenceEquals</c> 为真），
    /// 这是快路径：几百 KB 的文本不该为「什么都没做」白拷贝一份。
    /// null 返回 <see cref="string.Empty"/>，空串返回入参自身。
    /// </summary>
    /// <param name="text">待处理的文本，可空</param>
    /// <returns>切分后的文本；无需切分时就是入参本身</returns>
    public static string Wrap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (!NeedsWrap(text)) return text;

        // 每切一刀多一个 '\n'，按上限估个容量，免得 StringBuilder 一路翻倍扩容
        StringBuilder builder = new(text.Length + text.Length / MaxLineChars + 4);
        int lineStart = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;
            int contentEnd = ContentEnd(text, lineStart, lineEnd);

            int cursor = lineStart;
            while (contentEnd - cursor > MaxLineChars)
            {
                int cut = cursor + MaxLineChars;
                // 切点落在高位代理项上就往前挪一格：从代理对中间切开，两半都会渲染成乱码方块
                if (char.IsHighSurrogate(text[cut - 1])) cut--;
                builder.Append(text, cursor, cut - cursor).Append('\n');
                cursor = cut;
            }

            builder.Append(text, cursor, lineEnd - cursor); //末段连同行尾残留的 '\r' 一起原样带过去
            if (newline < 0) break;
            builder.Append('\n');
            lineStart = newline + 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// 行内容的结束下标（不含）。行尾若残留 <c>'\r'</c>（CRLF 的另一半）不计入长度：
    /// 它不占可见宽度，让它左右「切不切」只会让边界行为变得难以解释。
    /// </summary>
    private static int ContentEnd(string text, int lineStart, int lineEnd)
        => lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
}
