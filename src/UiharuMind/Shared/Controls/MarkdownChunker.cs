using System;
using System.Collections.Generic;
using Markdig;
using Markdig.Syntax;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 把一篇 markdown 按<b>顶层块边界</b>切成若干段。只在顶层块之间下刀，
/// 代码围栏、列表、表格、引用永远整块落在同一段里，每段单独解析的结果与整篇解析一致。
/// </summary>
public static class MarkdownChunker
{
    /// <summary>
    /// 切段
    /// </summary>
    /// <param name="text">整篇原文</param>
    /// <param name="chunkChars">每段的目标字数：攒够这么多就在下一个顶层块前下刀；单个块超长时整块成一段</param>
    /// <param name="pipeline">解析管线，须与渲染时用的一致，否则块边界可能对不上</param>
    /// <returns>按原顺序排列的各段原文，拼回去等于原文</returns>
    public static List<string> Split(string text, int chunkChars, MarkdownPipeline pipeline)
    {
        List<string> chunks = new();
        if (string.IsNullOrEmpty(text)) return chunks;
        if (text.Length <= chunkChars)
        {
            chunks.Add(text);
            return chunks;
        }

        MarkdownDocument document;
        try
        {
            document = Markdown.Parse(text, pipeline);
        }
        catch (Exception e)
        {
            // 病态输入（如上千层嵌套引用/列表、超大表格）会触发 Markdig 的深度限制而抛异常。
            // 这里只是找切块位置，预览绝不能跟着炸：退回不看语法的按行切分
            Log.Warning($"Markdown chunking fell back to plain split: {e.Message}");
            return SplitPlain(text, chunkChars);
        }

        int chunkStart = 0;
        foreach (Block block in document)
        {
            int blockStart = block.Span.Start;
            // 合成块（如脚注组）没有可靠位置，跳过不在它前面下刀
            if (blockStart <= chunkStart || blockStart >= text.Length) continue;
            if (blockStart - chunkStart < chunkChars) continue;

            chunks.Add(text[chunkStart..blockStart]);
            chunkStart = blockStart;
        }

        chunks.Add(text[chunkStart..]);
        return chunks;
    }

    /// <summary>
    /// 不看语法的兜底切分：攒够字数就在下一个换行处下刀。只保证拼回去等于原文，
    /// 不保证代码围栏/表格不断开——走到这里时原文已是 Markdig 解析不动的病态输入，
    /// 不炸比排得好看重要。单行超长（如巨型表格行）时硬切，避免切出巨型单段
    /// </summary>
    private static List<string> SplitPlain(string text, int chunkChars)
    {
        List<string> chunks = new();
        int chunkStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            bool cutAtNewline = text[i] == '\n' && i + 1 - chunkStart >= chunkChars;
            bool cutLongLine = i + 1 - chunkStart >= chunkChars * 2;
            if (!cutAtNewline && !cutLongLine) continue;

            int cut = i + 1;
            // 硬切别劈开代理项对
            if (cutLongLine && !cutAtNewline && cut < text.Length
                && char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1])) cut++;
            chunks.Add(text[chunkStart..cut]);
            chunkStart = cut;
        }

        if (chunkStart < text.Length) chunks.Add(text[chunkStart..]);
        return chunks;
    }
}
