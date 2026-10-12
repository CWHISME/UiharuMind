using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 切出来的一段
/// </summary>
/// <param name="Text">原文</param>
/// <param name="IsOversizedBlock">是从超长段落里硬切出来的：段落已被劈开，按 markdown 渲染会错配（如粗体标记），只适合纯文本显示</param>
public readonly record struct MarkdownChunk(string Text, bool IsOversizedBlock);

/// <summary>
/// 把一篇 markdown 按<b>顶层块边界</b>切成若干段。只在顶层块之间下刀，
/// 代码围栏、列表、表格、引用整块落在同一段里，每段单独解析的结果与整篇解析一致。
/// 例外是超长段落（如模型复读出的几十万字单段）：整块渲染会冻住界面，按行硬切并标记出来交给纯文本显示。
/// 只认段落：大代码块、大表格是正常内容，照旧整块渲染。
/// </summary>
public static class MarkdownChunker
{
    private const int OversizedPieceChars = 1024; //超长段落硬切的段长：复读文本缺换行点，排字成本随行长陡增，段要短

    private static readonly ConditionalWeakTable<MarkdownPipeline, MarkdownPipeline> BlockOnlyPipelines = new();

    /// <summary>
    /// 切段
    /// </summary>
    /// <param name="text">整篇原文</param>
    /// <param name="chunkChars">每段的目标字数：攒够这么多就在下一个顶层块前下刀</param>
    /// <param name="maxParagraphChars">段落上限：超过的段落不再整块成段，按行硬切成若干短段</param>
    /// <param name="pipeline">解析管线，须与渲染时用的一致，否则块边界可能对不上</param>
    /// <returns>按原顺序排列的各段，拼回去等于原文</returns>
    public static List<MarkdownChunk> Split(string text, int chunkChars, int maxParagraphChars, MarkdownPipeline pipeline)
    {
        List<MarkdownChunk> chunks = new();
        if (string.IsNullOrEmpty(text)) return chunks;
        if (text.Length <= chunkChars)
        {
            chunks.Add(new MarkdownChunk(text, false));
            return chunks;
        }

        MarkdownDocument document;
        try
        {
            document = Markdown.Parse(text, BlockOnlyPipelines.GetValue(pipeline, BuildBlockOnly));
        }
        catch (Exception e)
        {
            // 病态输入（如上千层嵌套引用/列表、超大表格）会触发 Markdig 的深度限制而抛异常。
            // 这里只是找切块位置，预览绝不能跟着炸：退回不看语法的按行切分
            Log.Warning($"Markdown chunking fell back to plain split: {e.Message}");
            foreach (string piece in SplitPlain(text, chunkChars)) chunks.Add(new MarkdownChunk(piece, false));
            return chunks;
        }

        // 顶层块起点；合成块（如脚注组）没有可靠位置，不在它前面下刀
        List<(int Start, bool IsParagraph)> blocks = new();
        foreach (Block block in document)
        {
            int blockStart = block.Span.Start;
            int previous = blocks.Count > 0 ? blocks[^1].Start : -1;
            if (blockStart > previous && blockStart < text.Length) blocks.Add((blockStart, block is ParagraphBlock));
        }

        if (blocks.Count == 0 || blocks[0].Start > 0) blocks.Insert(0, (0, false));

        int chunkStart = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            int blockStart = blocks[i].Start;
            int blockEnd = i + 1 < blocks.Count ? blocks[i + 1].Start : text.Length;
            if (blocks[i].IsParagraph && blockEnd - blockStart > maxParagraphChars)
            {
                if (blockStart > chunkStart) chunks.Add(new MarkdownChunk(text[chunkStart..blockStart], false));
                foreach (string piece in SplitPlain(text[blockStart..blockEnd], Math.Min(chunkChars, OversizedPieceChars)))
                    chunks.Add(new MarkdownChunk(piece, true));
                chunkStart = blockEnd;
                continue;
            }

            if (blockStart - chunkStart < chunkChars) continue;
            chunks.Add(new MarkdownChunk(text[chunkStart..blockStart], false));
            chunkStart = blockStart;
        }

        if (chunkStart < text.Length) chunks.Add(new MarkdownChunk(text[chunkStart..], false));
        return chunks;
    }

    // 切段只要顶层块的位置，段落的行内解析白做；复读出的成千上万个「**」还会让它撞上嵌套深度上限整篇抛异常。
    // 沿用渲染管线的全部扩展保证块边界一致，只关掉段落的行内解析：
    // 标题、脚注等块的扩展在行内阶段还要读它们的解析结果，关了会空引用。管道表格本是段落经行内阶段改写的，不改写也不动块边界
    private static MarkdownPipeline BuildBlockOnly(MarkdownPipeline pipeline)
    {
        MarkdownPipelineBuilder builder = new();
        foreach (IMarkdownExtension extension in pipeline.Extensions) builder.Extensions.Add(extension);
        builder.Extensions.Add(new SkipInlinesExtension());
        return builder.Build();
    }

    private sealed class SkipInlinesExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            foreach (BlockParser parser in pipeline.BlockParsers) parser.Closed += SkipInlines;
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
        }

        private static void SkipInlines(BlockProcessor processor, Block block)
        {
            if (block is ParagraphBlock paragraph) paragraph.ProcessInlines = false;
        }
    }

    /// <summary>
    /// 不看语法的兜底切分：攒够字数就在下一个换行处下刀。只保证拼回去等于原文，
    /// 不保证代码围栏/表格不断开——走到这里时原文已是 Markdig 解析不动的病态输入或超长单块，
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
