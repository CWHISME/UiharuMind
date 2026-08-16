/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 工具结果纯文本的截断视图：只有 <see cref="DisplayText"/> 会交给控件排版，
/// 被省掉的那部分由提示行代表。
/// </summary>
public readonly record struct ToolResultView
{
    /// <summary>真正参与文本排版的正文</summary>
    public string DisplayText { get; init; }

    /// <summary>原文是否超过阈值、被截过</summary>
    public bool IsTruncated { get; init; }

    /// <summary>原文总行数</summary>
    public int TotalLines { get; init; }

    /// <summary><see cref="DisplayText"/> 保下来的行数</summary>
    public int KeptLines { get; init; }

    /// <summary>被省掉的字符数（提示行按可读体积展示）</summary>
    public int OmittedChars { get; init; }
}

/// <summary>
/// 大结果的截断规则。
///
/// 为什么需要：工具结果面板外层的 <c>ScrollViewer MaxHeight</c> 只裁<b>视口</b>，不裁<b>文本布局</b>——
/// 一个 <c>SelectableTextBlock</c> + <c>TextWrapping=Wrap</c> 拿到几十万字，
/// 每次布局都要把整篇重新断行、重新量一遍，而会话流本身没有虚拟化，
/// 于是流式回复期间每一帧都在为一张早已跑完的卡片付这笔钱。
/// 头部截断把这笔钱压回常数级：看得见的信息几乎不损失（结果的关键内容基本都在开头），
/// 想看全文的人点一下再付。
///
/// 阈值刻意压得很低：全文现在一键就能在独立窗口里看（那边是 AvaloniaEdit，按行虚拟化），
/// 卡片预览的职责只剩「扫一眼知道跑成了什么」，不再兼任「读结果」。
///
/// 做成脱离控件的纯函数：它是<b>规则</b>而不是渲染，因此可以在不初始化 Avalonia 的前提下被测到。
/// 注意它只服务卡片预览，成本正比于<b>字符数</b>；全文窗那条规则（成本正比于<b>单行长度</b>）
/// 是另一套常数，两者刻意不合并，免得互相绑架。
/// </summary>
public static class ToolResultTruncation
{
    /// <summary>行数上限。280px 高的视口大约能显示 15 行，40 行够翻两三屏，扫一眼绰绰有余</summary>
    public const int MaxLines = 40;

    /// <summary>
    /// 字符数上限。与行数<b>先到者为准</b>：MCP/JSON 结果常常是一整行几百 KB，只看行数拦不住。
    /// 旧值 16KB 是照着「200 行视口」估出来的，宽了一个量级，而单行 minified JSON 绕开行数轴
    /// 直接顶满这 16KB；<c>TextWrapping=Wrap</c> 的排版成本正比于字符数，收到 2KB 等于把这笔钱降了八倍。
    /// </summary>
    public const int MaxChars = 2 * 1024;

    /// <summary>空结果的视图（无正文、未截断）</summary>
    public static ToolResultView Empty { get; } = new() { DisplayText = string.Empty };

    /// <summary>
    /// 按阈值构造结果的展示视图
    /// </summary>
    /// <param name="resultText">工具结果原文，可空</param>
    /// <returns>截断视图；未超阈值时 <see cref="ToolResultView.DisplayText"/> 就是原文本身</returns>
    public static ToolResultView Build(string? resultText)
    {
        if (string.IsNullOrEmpty(resultText)) return Empty;

        int cut = FindCut(resultText);
        int totalLines = CountLines(resultText);
        if (cut >= resultText.Length)
        {
            return new ToolResultView
            {
                DisplayText = resultText,
                TotalLines = totalLines,
                KeptLines = totalLines,
            };
        }

        return new ToolResultView
        {
            DisplayText = resultText[..cut],
            IsTruncated = true,
            TotalLines = totalLines,
            KeptLines = CountLines(resultText.AsSpan(0, cut)),
            OmittedChars = resultText.Length - cut,
        };
    }

    /// <summary>头部保留到哪个下标（不含）。返回值 >= 长度表示无需截断</summary>
    private static int FindCut(string text)
    {
        int index = 0;
        int lines = 0;
        while (index < text.Length && lines < MaxLines)
        {
            int newline = text.IndexOf('\n', index);
            int next = newline < 0 ? text.Length : newline + 1;
            // 这一行会吃光字符预算,就在行中间断开——一行几百 KB 的 JSON 照样能把排版拖死。
            // 曾经这里写的是 index == 0 ? MaxChars : index:不是第一行就退回上一行末尾,
            // 想的是"别把一行切两半"。代价是把整个预算白扔了——"Result:\n" + 500KB 单行
            // 的结果,预览只剩八个字符。行数预算是<b>上限</b>不是<b>目标</b>,字符预算同理,
            // 花满它才是本分
            if (next > MaxChars) return SafeCut(text, MaxChars);
            index = next;
            lines++;
        }

        return index;
    }

    /// <summary>
    /// 把切点挪到不会劈开代理对（surrogate pair）的位置。
    /// 劈开的后果是预览末尾留一个孤立代理项，渲染成乱码方块——中日文 emoji 结果上很容易撞到。
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="cut">期望切点（不含）</param>
    /// <returns>安全切点；落在高位代理项之后时往前挪一格</returns>
    private static int SafeCut(string text, int cut)
    {
        if (cut <= 0 || cut >= text.Length) return cut;
        return char.IsHighSurrogate(text[cut - 1]) ? cut - 1 : cut;
    }

    /// <summary>行数：按换行切分的段数，末尾换行不额外算一行</summary>
    private static int CountLines(ReadOnlySpan<char> text)
    {
        int lines = 0;
        int index = 0;
        while (index < text.Length)
        {
            int newline = text[index..].IndexOf('\n');
            index = newline < 0 ? text.Length : index + newline + 1;
            lines++;
        }

        return lines;
    }
}
