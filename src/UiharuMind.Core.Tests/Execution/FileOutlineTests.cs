using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住文件大纲：C# 只列类型与成员（不列字段、不进方法体、字符串注释里的括号不算数），
/// Markdown 只列代码块外的标题，超预算先裁最深层，Read 只在从头读且截断时附上
/// </summary>
public sealed class FileOutlineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-outline-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    private static List<OutlineEntry> CSharp(string code) =>
        new CSharpOutlineExtractor().Extract(code.Split('\n')).ToList();

    [Fact]
    public void CSharp_ListsTypesAndMembers_NotFieldsOrBodies()
    {
        List<OutlineEntry> entries = CSharp(""""
            namespace Demo;

            /// <summary>注释里的 { 不算</summary>
            public sealed class Foo : Bar
            {
                private int _count = Compute();
                public const string Name = "{";

                [Fact]
                public int Count => _count;

                public string Title { get; set; } = "}";

                public void Run(int a,
                    string b)
                {
                    void Local() { }
                    var s = @"{ "" }";
                    var r = $$"""
                        { {{a}} }
                        """;
                    char c = '{';
                }

                private enum EState { Idle, Busy }

                public record Item(int Id);
            }
            """");

        Assert.Equal(
        [
            new OutlineEntry(4, 0, "public sealed class Foo : Bar"),
            new OutlineEntry(10, 1, "public int Count"),
            new OutlineEntry(12, 1, "public string Title"),
            new OutlineEntry(14, 1, "public void Run(int a, string b)"),
            new OutlineEntry(25, 1, "private enum EState"),
            new OutlineEntry(27, 1, "public record Item(int Id)"),
        ], entries);
    }

    /// <summary>参数特性剥掉，数组类型与索引器参数保留</summary>
    [Fact]
    public void CSharp_StripsParameterAttributes_KeepsArraysAndIndexers()
    {
        List<OutlineEntry> entries = CSharp("""
            class Tools
            {
                public string Grep([Description("模式")] string pattern, int[] lines) => "";
                public int this[int index] => 0;
            }
            """);

        Assert.Equal("public string Grep(string pattern, int[] lines)", entries[1].Text);
        Assert.Equal("public int this[int index]", entries[2].Text);
    }

    [Fact]
    public void CSharp_AllmanBracesAndNestedTypes_TrackDepth()
    {
        List<OutlineEntry> entries = CSharp("""
            namespace Demo
            {
                internal static class Outer
                {
                    private sealed class Inner
                    {
                        public Inner()
                        {
                        }
                    }

                    public static void After() { }
                }
            }
            """);

        Assert.Equal(
        [
            new OutlineEntry(3, 0, "internal static class Outer"),
            new OutlineEntry(5, 1, "private sealed class Inner"),
            new OutlineEntry(7, 2, "public Inner()"),
            new OutlineEntry(12, 1, "public static void After()"),
        ], entries);
    }

    [Fact]
    public void Markdown_ListsHeadings_OutsideCodeFences()
    {
        List<OutlineEntry> entries = new MarkdownOutlineExtractor().Extract(
        [
            "## 安装", "```bash", "# 这是注释", "```", "#tag 不是标题", "### 第三步 ###",
        ]).ToList();

        Assert.Equal([new OutlineEntry(1, 1, "## 安装"), new OutlineEntry(6, 2, "### 第三步")], entries);
    }

    [Fact]
    public void Render_NormalizesDepth_AndSkipsUnsupportedTypes()
    {
        string? outline = FileOutline.Render("doc.md", ["## 安装", "### 依赖"]);

        Assert.Equal("文件大纲（行号: 结构，可传 offset 跳到对应行）：\n1: ## 安装\n2:   ### 依赖", outline);
        Assert.Null(FileOutline.Render("data.bin", ["## 安装"]));
        Assert.Null(FileOutline.Render("empty.md", ["正文"]));
    }

    [Fact]
    public void Render_OverBudget_DropsDeepestLevelFirst()
    {
        List<string> lines = [];
        for (int i = 0; i < 10; i++)
        {
            lines.Add($"# 章{i}");
            for (int j = 0; j < 10; j++) lines.Add($"## 节{i}.{j}");
        }

        string outline = FileOutline.Render("doc.md", lines)!;

        Assert.Contains("1: # 章0", outline);
        Assert.DoesNotContain("节", outline);
        Assert.Contains("更深的层级未列出", outline);
    }

    [Fact]
    public async Task Read_AttachesOutline_OnlyWhenTruncatedFromTheStart()
    {
        string path = Path.Combine(_dir, "big.md");
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 50).SelectMany(i => new[] { $"# 第{i}章", "正文" }),
            TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);

        string first = await tools.Read(path, 1, 10, TestContext.Current.CancellationToken);
        string paged = await tools.Read(path, 11, 10, TestContext.Current.CancellationToken);
        string whole = await tools.Read(path, 1, -1, TestContext.Current.CancellationToken);

        Assert.Contains("文件大纲", first);
        Assert.Contains("99: # 第50章", first);
        Assert.DoesNotContain("文件大纲", paged); //翻页时已经看过
        Assert.DoesNotContain("文件大纲", whole); //没截断不附
    }
}
