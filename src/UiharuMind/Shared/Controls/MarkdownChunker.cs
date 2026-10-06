using System.Collections.Generic;
using Markdig;
using Markdig.Syntax;

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

        MarkdownDocument document = Markdown.Parse(text, pipeline);
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
}
