using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// HTML 化 markdown：段落内换行当空白，列表保留，链接保留（GitHub 发布说明的 atom feed 就是这种 HTML）
/// </summary>
public class MarkdownCleanerTests
{
    [Fact]
    public void FeedHtml_BecomesParagraphsListsAndLinks()
    {
        const string html = """
            <p>A 1.0 loader has no vkEnumerateInstanceVersion, so<br>
            backend init called a <code>null</code> pointer.</p>
            <ul>
            <li>first item</li>
            <li>second <a class="issue-link" href="https://github.com/x/y/issues/1">#1</a></li>
            </ul>
            """;

        Assert.Equal("""
            A 1.0 loader has no vkEnumerateInstanceVersion, so

            backend init called a `null` pointer.

            - first item

            - second [#1](https://github.com/x/y/issues/1)
            """, MarkdownCleaner.Clean(html));
    }

    [Fact]
    public void PlainMarkdown_IsLeftAlone()
    {
        const string markdown = "# Title\n\n- a\n- b\n\n```html\n<div>x</div>\n```";
        Assert.Equal(markdown, MarkdownCleaner.Clean(markdown));
    }

    /// <summary>
    /// 带连字符的自定义元素(GitHub 的 turbo-frame/react-app/relative-time 等 Web Component)
    /// 名字不在标准标签里,旧正则 <c>[a-zA-Z][a-zA-Z0-9]*</c> 匹配到连字符就断,标签原样漏出。
    /// 元素名须允许连字符,标签删除、内容保留。
    /// </summary>
    [Fact]
    public void CustomElementsWithHyphens_AreStripped()
    {
        const string html =
            "<turbo-frame id=\"x\"><react-app app-name=\"issues-react\"><h1>标题</h1>" +
            "<p>正文 <relative-time>on Oct 5</relative-time>。</p></react-app></turbo-frame>";

        string cleaned = MarkdownCleaner.Clean(html);

        Assert.DoesNotContain("<turbo-frame", cleaned);
        Assert.DoesNotContain("<react-app", cleaned);
        Assert.DoesNotContain("relative-time", cleaned);
        Assert.Contains("# 标题", cleaned);
        Assert.Contains("on Oct 5", cleaned);
    }
}
