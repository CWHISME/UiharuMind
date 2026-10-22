using System.Text;
using UiharuMind.Shared.Windows;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 文本窗的文档模型：叫什么、在哪、按什么语言、算不算正文、另存为默认叫什么
/// </summary>
public class TextFileDocumentTests
{
    [Fact]
    public void File_IsNamedByItsFileName_AndAddressedByItsPath()
    {
        // 用平台真正的绝对路径：/repo/src/A.cs 这种 POSIX 写法在 Windows 上
        // 不被 Path.IsPathFullyQualified 认作绝对路径，LinkBaseDirectory 会落空
        string dir = Path.Combine(Path.GetTempPath(), "repo", "src");
        string file = Path.Combine(dir, "A.cs");
        TextFileDocument document = TextFileDocument.ForFile(file, new UTF8Encoding(false), false);

        Assert.Equal("A.cs", document.GetTitle("未命名"));
        Assert.Equal(file, document.GetLocation("未命名"));
        Assert.Equal(dir, document.LinkBaseDirectory);
        Assert.False(document.IsReadOnly);
    }

    [Fact]
    public void ReadOnlyText_UsesItsTitle_AndShowsWhereItCameFrom()
    {
        TextFileDocument document = TextFileDocument.ReadOnly("Read · 结果", "docs/readme.md");

        Assert.Equal("Read · 结果", document.GetTitle("未命名"));
        Assert.Equal("docs/readme.md", document.GetLocation("未命名"));
        Assert.Null(document.LinkBaseDirectory); //相对路径无从解析
        Assert.True(document.IsReadOnly);
    }

    [Theory]
    [InlineData("a.cs", false, false)]
    [InlineData("a.MD", true, true)]
    [InlineData("a.txt", true, false)]
    [InlineData(null, true, false)]
    public void ProseAndMarkdown_FollowTheLanguageSource(string? source, bool prose, bool markdown)
    {
        TextFileDocument document = TextFileDocument.ReadOnly("t", source);

        Assert.Equal(prose, document.IsProse);
        Assert.Equal(markdown, document.IsMarkdown);
    }

    [Fact]
    public void DefaultSaveName_PrefersTheFile_ThenTheSource_ThenWhatIsShown()
    {
        Assert.Equal("A.cs", TextFileDocument.ForFile("/repo/A.cs", Encoding.UTF8, false).GetDefaultSaveName(false));
        Assert.Equal("readme.md", TextFileDocument.ReadOnly("t", "docs/readme.md").GetDefaultSaveName(false));
        Assert.Equal("untitled.md", TextFileDocument.ReadOnly("t", null).GetDefaultSaveName(previewing: true));
        Assert.Equal("untitled.txt", TextFileDocument.Untitled().GetDefaultSaveName(previewing: false));
    }

    [Fact]
    public void EncodingDisplay_UsesConventionalNames_AndMarksTheBom()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Assert.Equal("GB18030", TextFileDocument.ForFile("/a.txt", Encoding.GetEncoding("gb18030"), false).EncodingDisplay);
        Assert.Equal("UTF-8 (BOM)", TextFileDocument.ForFile("/a.txt", new UTF8Encoding(true), true).EncodingDisplay);
    }

    [Fact]
    public void SavingAs_MovesTheFileToTheNewPath_AndClearsTheChanges()
    {
        TextFileDocument document = TextFileDocument.Untitled();
        document.IsDirty = true;

        document.MoveTo("/repo/new.md");

        Assert.Equal("/repo/new.md", document.FilePath);
        Assert.False(document.IsDirty);
        Assert.True(document.IsMarkdown);
    }
}
