using UiharuMind.Core.AI.Models.Sources;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 模型卡整理：去掉开头的 YAML 元数据与 HTML 注释，正文原样
/// </summary>
public class ModelReadmeTests
{
    [Fact]
    public void StripsFrontMatterAndComments()
    {
        const string readme = "---\r\nlicense: apache-2.0\r\ntags:\r\n- gguf\r\n---\r\n<!-- header -->\r\n# Qwen3\r\n\r\nBody --- text";

        Assert.Equal("# Qwen3\n\nBody --- text", ModelReadme.Clean(readme));
    }

    [Fact]
    public void WithoutFrontMatter_KeepsLeadingRule()
    {
        Assert.Equal("# Title\n\n---\n\nmore", ModelReadme.Clean("# Title\n\n---\n\nmore"));
    }

    [Fact]
    public void HtmlHeader_BecomesMarkdown_ImagesDropped()
    {
        const string readme = """
            <div>
              <p style="margin: 0;">
                <strong>See <a href="https://hf.co/c">our collection</a> for all versions & formats.</strong>
              </p>
              <div style="display: flex;">
                <a href="https://github.com/x">
                  <img src="https://x/logo.png" width="133">
                </a>
              </div>
            <h1 style="margin-top: 0rem;">✨ Run Qwen3!</h1>
            </div>

            - item with <b>bold</b><br>and break

            ```html
            <div>keep me</div>
            ```
            """;

        Assert.Equal("""
            **See [our collection](https://hf.co/c) for all versions & formats.**

            # ✨ Run Qwen3!

            - item with **bold** and break

            ```html
            <div>keep me</div>
            ```
            """, ModelReadme.Clean(readme));
    }
}
