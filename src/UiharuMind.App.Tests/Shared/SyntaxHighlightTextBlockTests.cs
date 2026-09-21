using Avalonia.Controls.Documents;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 语言来源解析：只认文件名/扩展名的确定信息，不猜内容（docs/CONTEXT.md「结果的语言来源」）。
/// 渲染逻辑本身（Inlines 构建）依赖 UI 线程，按 App.Tests 口径不测
/// </summary>
public class SyntaxHighlightTextBlockTests
{
    [Theory]
    [InlineData("Foo.cs", "cs")]
    [InlineData("/abs/path/Foo.cs", "cs")]
    [InlineData("dev/Foo.cs", "cs")]
    [InlineData(".json", "json")]
    [InlineData("json", "json")]
    [InlineData("Foo.TXT", "txt")]
    public void ResolveLanguageName_ExtractsExtensionOrBareWord(string source, string expected)
    {
        Assert.Equal(expected, SyntaxHighlightTextBlock.ResolveLanguageName(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveLanguageName_Blank_ReturnsNull(string? source)
    {
        Assert.Null(SyntaxHighlightTextBlock.ResolveLanguageName(source));
    }

    [Fact]
    public void ResolveLanguageName_ExtensionlessPath_FallsThroughToBareWord()
    {
        // README / 目录名这类裸词也会产出语言名；SyntaxHighlighting 认不出就自然退化纯文本，
        // 这里只钉解析规则，不钉运行时认不认识某个语言
        Assert.Equal("readme", SyntaxHighlightTextBlock.ResolveLanguageName("README"));
    }

    [Fact]
    public void FormatInlines_ColorsKnownLanguage()
    {
        // cs：C# 片段会被拆分出结构色（public 命中关键字蓝）——钉住「上色不是空跑」。
        // FormatInlines 会把多 token 的行包进 Span，所以递归下到 Span 内部才算数
        var inlines = new InlineCollection();
        inlines.Add(new Run("public class A { }"));
        SyntaxHighlighting.Create("cs").FormatInlines(inlines, ThemeName.LightPlus);

        var runs = EnumerateRuns(inlines).ToList();
        Assert.Contains(runs, r => r.Text == "public" && r.Foreground is not null);
    }

    [Fact]
    public void Yaml_ColorsKeyAndNumericValue_JsonColorsOnlyTheNumber()
    {
        // 参数区是扁平 "key: value"（不是 JSON）。yaml 把键与数字值分开着色，
        // json 只会把 4 染成假数字绿——参数区选 yaml 不选 json 的依据（ADR 0035）
        static string[] DistinctForegrounds(string grammar, string line)
        {
            var inlines = new InlineCollection();
            inlines.Add(new Run(line));
            SyntaxHighlighting.Create(grammar).FormatInlines(inlines, ThemeName.LightPlus);
            return EnumerateRuns(inlines)
                .Where(r => !string.IsNullOrEmpty(r.Text))
                .Select(r => r.Foreground?.ToString() ?? "null")
                .Distinct().OrderBy(x => x).ToArray();
        }

        string[] yaml = DistinctForegrounds("yaml", "contextLines: 4");
        Assert.True(yaml.Length >= 2, $"yaml 没把键与值分出颜色: {string.Join(",", yaml)}");
    }

    [Fact]
    public void FormatInlines_UnknownLanguage_FallsBackToDefaultForeground()
    {
        // 认不出的名字也走 fallback 语法：整段一个 token、不产生结构色拆分——
        // 目录名/无扩展名结果因此不会得到「花哨但错误」的颜色（安全降级）
        var inlines = new InlineCollection();
        inlines.Add(new Run("some text"));
        SyntaxHighlighting.Create("definitelynotalanguage").FormatInlines(inlines, ThemeName.LightPlus);

        var run = Assert.Single(EnumerateRuns(inlines));
        Assert.NotNull(run.Foreground);
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                yield return run;
            }
            else if (inline is Span span)
            {
                foreach (var nested in EnumerateRuns(span.Inlines))
                {
                    yield return nested;
                }
            }
        }
    }
}