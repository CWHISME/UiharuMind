using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using LiveMarkdown.Avalonia;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// <c>MainFont</c> 的族名必须和字体文件里的族名<b>一字不差</b>，
/// 且 Bold 请求必须能拿到粗体（真字面或合成）。成因见 docs/adr/0040。
///
/// 背景（字体结论以真实进程 diag.font 为准，无头层开真 Skia 后字体解析已真实，但历史教训保留）：
/// 1. 族名写作 <c>#HarmonyOS Sans</c>（缺 <c>SC</c>）实测表现一样，
///    不是「全应用没粗体」的根因，但真实族名才可预期；
/// 2. 曾只有 Regular 一个字面，Bold 是合成粗体（只描粗、不改字宽，中文几乎看不出），
///    所以补了同族名的真 Bold 字面（usWeightClass=700）。
/// 这条守的是底线：Bold 请求至少拿到 ≥700 的字重或合成粗体，别静默退化。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class MainFontTests(ITestOutputHelper output)
{
    /// <param name="weight">字重</param>
    /// <param name="block">受测文本块，null 则用普通 TextBlock 配 MainFont</param>
    /// <returns>该字重下实际拿到的字形字体</returns>
    private static GlyphTypeface Resolve(FontWeight weight, TextBlock? block = null)
    {
        FontFamily mainFont = (FontFamily)Application.Current!.FindResource("MainFont")!;
        block ??= new TextBlock { FontFamily = mainFont };
        block.Text = "修了什么 Bold";
        block.FontWeight = weight;
        block.FontSize = 20;
        Window window = new() { Width = 600, Height = 200, Content = block };
        window.Show();
        window.UpdateLayout();
        GlyphTypeface typeface = block.TextLayout.TextLines
            .SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().First().GlyphRun.GlyphTypeface;
        window.Close();
        return typeface;
    }

    [Fact]
    public void MainFont_ActuallyRendersBold() => HeadlessUi.Run(() =>
    {
        GlyphTypeface normal = Resolve(FontWeight.Normal);
        GlyphTypeface bold = Resolve(FontWeight.Bold);

        output.WriteLine($"Normal → {normal.FamilyName} 字重={normal.Weight} 模拟={normal.FontSimulations}");
        output.WriteLine($"Bold   → {bold.FamilyName} 字重={bold.Weight} 模拟={bold.FontSimulations}");

        Assert.Equal(FontSimulations.None, normal.FontSimulations);
        Assert.True(bold.Weight >= FontWeight.Bold || bold.FontSimulations.HasFlag(FontSimulations.Bold),
            $"MainFont 拿不到粗体(字重 {bold.Weight}/模拟 {bold.FontSimulations})——" +
            "多半是字体资源缺 Bold 字面，或 MainFont 没解析到资源里的字面");
    });

    /// <summary>
    /// markdown 也得拿得到粗体，而且用的必须是同一个族。
    ///
    /// 症状曾经是「同一个 <c>**XXX**</c> 一半粗一半不粗」：LiveMarkdown 硬写的
    /// <c>Arial, 'Segoe UI', sans-serif</c> 不含中文字形，英文在族内匹配拿得到粗，
    /// 中文走<b>字符回退</b>——而 Avalonia 的回退路径把合成粗体写死关掉了
    /// （<c>FontManagerImpl.TryMatchCharacter</c> 里 <c>FontSimulations.None</c>，
    /// 族内那条路却会算 <c>(int)weight >= 600</c>）。用含中文字形的族，两种字符都走族内匹配。
    /// </summary>
    [Fact]
    public void Markdown_UsesMainFont_AndRendersBold() => HeadlessUi.Run(() =>
    {
        GlyphTypeface plain = Resolve(FontWeight.Bold);
        GlyphTypeface markdown = Resolve(FontWeight.Bold, new MarkdownTextBlock());

        output.WriteLine($"正文     → {plain.FamilyName} 模拟={plain.FontSimulations}");
        output.WriteLine($"markdown → {markdown.FamilyName} 模拟={markdown.FontSimulations}");

        Assert.Equal(plain.FamilyName, markdown.FamilyName);
        Assert.True(markdown.Weight >= FontWeight.Bold || markdown.FontSimulations.HasFlag(FontSimulations.Bold),
            $"markdown 拿不到粗体(字重 {markdown.Weight}/模拟 {markdown.FontSimulations})——" +
            "多半是 CustomMarkdownStyle.axaml 没挂上，或者挂在 LiveMarkdown 之前了");
    });
}
