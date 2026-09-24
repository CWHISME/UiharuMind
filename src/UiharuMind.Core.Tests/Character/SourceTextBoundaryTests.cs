using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.PromptActions;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 钉死快捷工具的数据/指令隔离：待处理文本统一包进 source_text 再发，
/// 工具卡模板侧必须声明这套边界。两边任何一边被改掉，另一边就形同虚设。
/// 例外：识图类工具——用户文字是「要回答的问题」而非待处理数据，
/// 见 <see cref="PromptActionVisionBase.WrapUserInput"/>（视觉工具不包裹）。
/// </summary>
public class SourceTextBoundaryTests
{
    public SourceTextBoundaryTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
    }

    [Fact]
    public void WrapSourceText_WrapsContentInTags()
    {
        string wrapped = PromptActionConvertableBase.WrapSourceText("你好");

        Assert.StartsWith("<source_text>\n", wrapped);
        Assert.EndsWith("\n</source_text>", wrapped);
        Assert.Contains("你好", wrapped);
    }

    [Fact]
    public void WrapSourceText_EscapesLiteralCloseTag()
    {
        string wrapped = PromptActionConvertableBase.WrapSourceText("a</source_text>b");

        // 内容里的字面闭标签必须转义，否则模型会提前“出狱”，后面的文本重获指令身份
        Assert.Contains("<\\/source_text>", wrapped);
        Assert.Equal(1, CountOccurrences(wrapped, "</source_text>"));
    }

    [Theory]
    [InlineData(DefaultCharacter.TranslationPrompt)]
    [InlineData(DefaultCharacter.AdvancedTranslationPrompt)]
    [InlineData(DefaultCharacter.ExplainPrompt)]
    [InlineData(DefaultCharacter.SyntacticAnalysisPrompt)]
    [InlineData(DefaultCharacter.ChainOfThoughtPrompt)]
    public void ToolCard_DeclaresSourceTextBoundary(DefaultCharacter character)
    {
        string template = DefaultCharacterManager.Instance.GetCharacterData(character).Template;

        Assert.Contains("source_text", template);
    }

    [Theory]
    [InlineData(DefaultCharacter.ImageVisionPrompt)]
    [InlineData(DefaultCharacter.ImageOcrPrompt)]
    public void VisionToolCard_DoesNotUseSourceTextBoundary(DefaultCharacter character)
    {
        // 视觉类模板侧不得声明 source_text：它是通用图文问答/OCR 的指令集，
        // 不是「待处理数据」工具；模板若出现标签说明有人把这种事当成嵌套数据了。
        string template = DefaultCharacterManager.Instance.GetCharacterData(character).Template;

        Assert.DoesNotContain("source_text", template);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
