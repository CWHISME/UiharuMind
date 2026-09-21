/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 超长单行的硬切规则。
///
/// 它解决的是全文窗那一侧的成本：AvaloniaEdit 按<b>行</b>虚拟化，一行 480KB 的 minified JSON
/// 仍然只是一个 VisualLine，照样要整行测量——虚拟化救不了。切成 480 行它才生效。
///
/// 两处最容易写错、也最该被钉住的：切点不许落在代理对中间（切坏了是乱码方块），
/// 以及「没超长」时必须原样返回同一引用（几百 KB 白拷贝一份就白省了）。
/// </summary>
public class LongLineWrapperTests
{
    private const int Limit = LongLineWrapper.MaxLineChars;

    /// <summary>null 不该炸，也不该凭空造出内容</summary>
    [Fact]
    public void NullText_NeedsNoWrapAndBecomesEmpty()
    {
        Assert.False(LongLineWrapper.NeedsWrap(null));
        Assert.Equal(string.Empty, LongLineWrapper.Wrap(null));
    }

    /// <summary>空串走的是同一条快路径：原样返回</summary>
    [Fact]
    public void EmptyText_PassesThroughUntouched()
    {
        string text = string.Empty;

        Assert.False(LongLineWrapper.NeedsWrap(text));
        Assert.Same(text, LongLineWrapper.Wrap(text));
    }

    /// <summary>
    /// 快路径的本体：没有超长行时必须返回<b>同一引用</b>。
    /// 这不是锦上添花——全文窗常拿到几百 KB 的短行文本，白拷贝一份就把省下的钱又花回去了。
    /// </summary>
    [Fact]
    public void NoLongLine_ReturnsTheSameReference()
    {
        string text = string.Join('\n', Enumerable.Range(0, 500).Select(i => $"line {i}"));

        Assert.False(LongLineWrapper.NeedsWrap(text));
        Assert.Same(text, LongLineWrapper.Wrap(text));
    }

    /// <summary>恰好压在上限上不切：阈值是「超过才切」，差一个字符就是 off-by-one</summary>
    [Fact]
    public void ExactlyAtTheLimit_IsNotWrapped()
    {
        string text = new('x', Limit);

        Assert.False(LongLineWrapper.NeedsWrap(text));
        Assert.Same(text, LongLineWrapper.Wrap(text));
    }

    /// <summary>上限 + 1：切成两行，第二行只有一个字符</summary>
    [Fact]
    public void OneCharOverTheLimit_BecomesTwoLines()
    {
        string text = new('x', Limit + 1);

        Assert.True(LongLineWrapper.NeedsWrap(text));
        string[] lines = LongLineWrapper.Wrap(text).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal(Limit, lines[0].Length);
        Assert.Equal("x", lines[1]);
    }

    /// <summary>2.5 倍上限切成「满、满、余数」三行，且一个字符都不许丢</summary>
    [Fact]
    public void LongLine_IsCutIntoFullChunksPlusRemainder()
    {
        const int remainder = 500;
        string text = new('x', Limit * 2 + remainder);

        string wrapped = LongLineWrapper.Wrap(text);
        string[] lines = wrapped.Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal(Limit, lines[0].Length);
        Assert.Equal(Limit, lines[1].Length);
        Assert.Equal(remainder, lines[2].Length);
        Assert.Equal(text, wrapped.Replace("\n", string.Empty)); //切分是纯插入，原文一字未改
    }

    /// <summary>
    /// 切点正落在代理对中间时往前挪一格。切坏了不是「显示得难看一点」，
    /// 而是前后两行各多出一个乱码方块，肉眼一看就是 bug。
    /// </summary>
    [Fact]
    public void SurrogatePair_IsNeverSplitApart()
    {
        // 让 emoji 的高位代理项正好落在第 Limit 个字符上（下标 Limit-1）
        string text = new string('a', Limit - 1) + "😀" + new string('b', 200);

        string[] lines = LongLineWrapper.Wrap(text).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal(Limit - 1, lines[0].Length); //为了不切开代理对，第一行比上限短一格
        Assert.False(char.IsHighSurrogate(lines[0][^1]));
        Assert.StartsWith("😀", lines[1]);
    }

    /// <summary>行尾的 <c>'\r'</c> 不计入长度：它不占可见宽度，让它左右边界只会让行为变得没法解释</summary>
    [Fact]
    public void TrailingCarriageReturn_DoesNotCountTowardTheLimit()
    {
        string text = new string('x', Limit) + "\r\n" + "tail";

        Assert.False(LongLineWrapper.NeedsWrap(text));
        Assert.Same(text, LongLineWrapper.Wrap(text));
    }

    /// <summary>CRLF 文本被切之后，<c>'\r'</c> 仍旧贴在原来那行的末尾，既不吞掉也不挪位</summary>
    [Fact]
    public void CarriageReturns_SurviveTheCut()
    {
        string text = "short\r\n" + new string('x', Limit + 1) + "\r\ntail";

        string[] lines = LongLineWrapper.Wrap(text).Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.Equal("short\r", lines[0]);
        Assert.Equal(new string('x', Limit), lines[1]); //切出来的中间段不该凭空多出 '\r'
        Assert.Equal("x\r", lines[2]);
        Assert.Equal("tail", lines[3]);
    }

    /// <summary>短行 + 超长行 + 短行：只有超长的那行被动过</summary>
    [Fact]
    public void MixedLines_OnlyTheLongOneIsCut()
    {
        string text = "head\n" + new string('x', Limit * 2) + "\ntail";

        string[] lines = LongLineWrapper.Wrap(text).Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.Equal("head", lines[0]);
        Assert.Equal(Limit, lines[1].Length);
        Assert.Equal(Limit, lines[2].Length);
        Assert.Equal("tail", lines[3]);
    }

    /// <summary>
    /// 一整行几百 KB（MCP 结果的常态）：切成几百行，虚拟化才有东西可省。
    /// 顺带钉住实现是单次扫描——写成反复 Substring 拼接的 O(n²)，这条用例会先慢到不像话。
    /// </summary>
    [Fact]
    public void HugeSingleLine_IsCutIntoManyLines()
    {
        const int fullChunks = 500;
        const int remainder = 123;
        string text = new('j', Limit * fullChunks + remainder);

        string wrapped = LongLineWrapper.Wrap(text);
        string[] lines = wrapped.Split('\n');

        Assert.Equal(fullChunks + 1, lines.Length);
        Assert.All(lines[..fullChunks], line => Assert.Equal(Limit, line.Length));
        Assert.Equal(remainder, lines[^1].Length);
        Assert.Equal(text.Length + fullChunks, wrapped.Length); //只多了插进去的那些 '\n'
    }
}
