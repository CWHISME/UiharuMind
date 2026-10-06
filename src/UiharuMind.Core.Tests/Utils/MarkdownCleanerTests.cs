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
}
