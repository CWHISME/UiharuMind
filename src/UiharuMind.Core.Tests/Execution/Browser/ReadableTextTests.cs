using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 正文交给模型前收一道：链接只留字（维基的地址是百分号编码的相对路径，一个几十字节）、空白行并成段落间距、代码原样
/// </summary>
public class ReadableTextTests
{
    [Fact]
    public void LinksKeepTheirText_IncludingNestedBracketsAndParentheses()
    {
        string markdown = "白井黑子是[风纪委员](/wiki/%E9%A2%A8%E7%B4%80)。[[17]](#cite_note-17) " +
                          "见[Foo (bar)](/wiki/Foo_(bar) \"title\")。";

        Assert.Equal("白井黑子是风纪委员。[17] 见Foo (bar)。", ReadableText.Tidy(markdown));
    }

    [Fact]
    public void BlankLinesCollapse()
    {
        string markdown = "\t\t\n# Title\n\t\n  \n\n![Logo](/img/logo.png)\nBody";

        Assert.Equal("# Title\n\nLogo\nBody", ReadableText.Tidy(markdown));
    }

    /// <summary>代码里的 d[k](x) 不是链接；没配对的方括号不跨行吞内容</summary>
    [Fact]
    public void CodeIsLeftAlone_AndUnpairedBracketsStopAtTheLineEnd()
    {
        string markdown = "Call `handlers[name](event)` here.\n```\nd[k](x)\n```\nRange [0, 1) and\n[next](/n).";

        Assert.Equal("Call `handlers[name](event)` here.\n```\nd[k](x)\n```\nRange [0, 1) and\nnext.",
            ReadableText.Tidy(markdown));
    }
}
