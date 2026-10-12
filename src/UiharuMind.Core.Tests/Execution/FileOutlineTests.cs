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

    // 结构用例只比行号、深度与文本，结束行由专门的用例钉
    private static List<OutlineEntry> CSharp(string code) =>
        new CSharpOutlineExtractor().Extract(code.Split('\n')).Select(entry => entry with { EndLine = 0 }).ToList();

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

    /// <summary>表达式体与初始化器里的 { } 是表达式不是块：属性模式、对象初始化器、switch 表达式；参数默认值后的 { 仍是方法体</summary>
    [Fact]
    public void CSharp_BracesInsideExpressions_DoNotOpenBlocks()
    {
        List<OutlineEntry> entries = new CSharpOutlineExtractor().Extract("""
            class Foo
            {
                private bool IsOwn(string id) =>
                    Current?.Id is { } self && self == id;

                private readonly Bar _bar = new() { A = 1 };

                public string Name => _kind switch { 1 => "a", _ => "b" };

                public void Run(int a = 1)
                {
                }
            }
            """.Split('\n')).ToList();

        Assert.Equal(
        [
            new OutlineEntry(1, 0, "class Foo", 13),
            new OutlineEntry(3, 1, "private bool IsOwn(string id)", 4),
            new OutlineEntry(8, 1, "public string Name", 8),
            new OutlineEntry(10, 1, "public void Run(int a = 1)", 12),
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
    public void CSharp_AllmanBracesAndNestedTypes_TrackDepthAndEndLines()
    {
        List<OutlineEntry> entries = new CSharpOutlineExtractor().Extract("""
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
            """.Split('\n')).ToList();

        Assert.Equal(
        [
            new OutlineEntry(3, 0, "internal static class Outer", 13),
            new OutlineEntry(5, 1, "private sealed class Inner", 10),
            new OutlineEntry(7, 2, "public Inner()", 9),
            new OutlineEntry(12, 1, "public static void After()", 12),
        ], entries);
    }

    private static List<OutlineEntry> Cpp(string code) =>
        new CppOutlineExtractor().Extract(code.Split('\n')).Select(entry => entry with { EndLine = 0 }).ToList();

    [Fact]
    public void Cpp_ListsTypesAndFunctions_NotFieldsOrBodies()
    {
        List<OutlineEntry> entries = Cpp(""""
            #define WRAP(x) \
                { x; }
            namespace demo {
            class Fwd;
            template <typename T, class U = std::vector<T>>
            class Foo : public Bar
            {
            public:
                Foo(int a) : _a(a), _b{a} {}
                [[nodiscard]] int Count() const;
                Foo& operator=(const Foo&) = default;
                virtual void Run() = 0;
            private:
                int _a = 1'000;
                int _b{0};
                enum class EState : int { Idle, Busy };
            };

            int Foo::Count() const
            {
                auto s = R"x( } ; )x";
                char c = '{';
                return _a;
            }

            void Helper(int a,
                        int b);
            static_assert(sizeof(int) == 4);
            }
            """");

        Assert.Equal(
        [
            new OutlineEntry(5, 0, "template <typename T, class U = std::vector<T>> class Foo : public Bar"),
            new OutlineEntry(9, 1, "Foo(int a)"),
            new OutlineEntry(10, 1, "int Count() const"),
            new OutlineEntry(11, 1, "Foo& operator=(const Foo&) = default"),
            new OutlineEntry(12, 1, "virtual void Run() = 0"),
            new OutlineEntry(16, 1, "enum class EState : int"),
            new OutlineEntry(19, 0, "int Foo::Count() const"),
            new OutlineEntry(26, 0, "void Helper(int a, int b)"),
        ], entries);
    }

    [Fact]
    public void Markdown_ListsHeadings_OutsideCodeFences()
    {
        List<OutlineEntry> entries = new MarkdownOutlineExtractor().Extract(
        [
            "## 安装", "```bash", "# 这是注释", "```", "#tag 不是标题", "### 第三步 ###",
        ]).ToList();

        Assert.Equal([new OutlineEntry(1, 1, "## 安装", 6), new OutlineEntry(6, 2, "### 第三步", 6)], entries);
    }

    /// <summary>一节到下一个同级或更高级标题之前为止</summary>
    [Fact]
    public void Markdown_SectionEndsBeforeNextHeadingOfSameOrHigherLevel()
    {
        List<OutlineEntry> entries = new MarkdownOutlineExtractor().Extract(
            ["# A", "## B", "正文", "## C", "# D", "正文"]).ToList();

        Assert.Equal(
        [
            new OutlineEntry(1, 0, "# A", 4),
            new OutlineEntry(2, 1, "## B", 3),
            new OutlineEntry(4, 1, "## C", 4),
            new OutlineEntry(5, 0, "# D", 6),
        ], entries);
        Assert.Equal("## B", FileOutline.Enclosing(entries, 3)?.Text);
        Assert.Equal("# A", FileOutline.Enclosing(entries, 1)?.Text);
    }

    [Fact]
    public void Render_NormalizesDepth_AndSkipsUnsupportedTypes()
    {
        string? outline = FileOutline.Render("doc.md", ["## 安装", "### 依赖"]);

        Assert.Equal("文件大纲（[起–止行] 结构，可传 offset 与 head 只读对应的段）：\n[1–2] ## 安装\n[2]   ### 依赖", outline);
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

        Assert.Contains("\n[1–11] # 章0", outline);
        Assert.DoesNotContain("节", outline);
        Assert.Contains("更深的层级未列出", outline);
    }

    /// <summary>截断附的大纲：已显示的段不再列；一个文件一个类时裁不掉它的成员层，放不下就截尾并指向 outline=true</summary>
    [Fact]
    public void Render_ForTruncatedRead_SkipsShownAndKeepsMembersOfSoleRoot()
    {
        List<string> lines = ["class Big", "{"];
        for (int i = 0; i < 80; i++)
        {
            lines.Add($"    void M{i}()");
            lines.Add("    {");
            lines.Add("        Inner();");
            lines.Add("    }");
        }

        lines.Add("}");

        string outline = FileOutline.Render("big.cs", lines, shownThrough: 42)!;

        Assert.Contains("已显示的第 1–42 行", outline);
        Assert.DoesNotContain("void M9()", outline); //第 39–42 行，已显示
        Assert.Contains("[43–46]   void M10()", outline);
        Assert.Contains("要看完整大纲传 outline=true", outline);
    }

    [Fact]
    public async Task Read_AttachesOutline_OnlyWhenDefaultWindowTruncatesFromTheStart()
    {
        // 50 章各 50 行，共 2500 行，默认窗口 2000 行读不完
        string path = Path.Combine(_dir, "big.md");
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 50).SelectMany(i => Enumerable.Repeat("正文", 49).Prepend($"# 第{i}章")),
            TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);
        CancellationToken ct = TestContext.Current.CancellationToken;

        string first = await tools.Read(path, cancellationToken: ct);
        string paged = await tools.Read(path, 2001, cancellationToken: ct);
        string withHead = await tools.Read(path, 1, 10, cancellationToken: ct);
        string whole = await tools.Read(path, 1, -1, cancellationToken: ct);

        Assert.Contains("[2451–2500] # 第50章", first);
        Assert.DoesNotContain("文件大纲", paged); //翻页时已经看过
        Assert.DoesNotContain("文件大纲", withHead); //自己传 head 的不多付这份开销
        Assert.DoesNotContain("文件大纲", whole); //没截断不附
    }

    [Fact]
    public async Task Read_OutlineMode_ReturnsOutlineOnly()
    {
        string path = Path.Combine(_dir, "a.cs");
        await File.WriteAllTextAsync(path, "class A\n{\n    void Run()\n    {\n    }\n}\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "x", TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);

        string outline = await tools.Read(path, outline: true, cancellationToken: TestContext.Current.CancellationToken);
        string unsupported = await tools.Read("a.txt", outline: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("文件大纲（[起–止行] 结构，可传 offset 与 head 只读对应的段）：\n[1–6] class A\n[3–5]   void Run()", outline);
        Assert.Contains("没有大纲", unsupported);
        Assert.EndsWith("已忽略 offset 与 head]",
            await tools.Read(path, 3, 2, outline: true, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>命中前标出所在结构，同一结构里的连续命中只标一次；命中落在声明行（查定义）时标外层</summary>
    [Fact]
    public async Task Grep_AnnotatesEnclosingStructure()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "b.cs"), """
            class B
            {
                void Run()
                {
                    Hit(1);
                    Hit(2);
                }

                void Stop() { Hit(3); }
            }

            class Other { }
            """, TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);

        string result = await tools.Grep("Hit\\(", ct: TestContext.Current.CancellationToken);

        Assert.Contains("[位于 3–7] void Run()\n5:        Hit(1);\n6:        Hit(2);\n[位于 1–10] class B\n9:", result);
    }

    /// <summary>只有一个顶层类时不为它标（几乎每处命中都在它里面）；声明行已显示时不再重复标</summary>
    [Fact]
    public async Task Grep_SkipsSoleRootAndAlreadyShownDeclarations()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "d.cs"), """
            class D
            {
                private int _hit;

                void Run()
                {
                    _hit++;
                }
            }
            """, TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);

        string result = await tools.Grep("_hit|Run", ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("[位于", result);
        Assert.Contains("3:    private int _hit;\n5:    void Run()\n7:        _hit++;", result);
    }

    /// <summary>标注加上去会超预算时整段不加，正文照常返回，不能被挤成命中地图</summary>
    [Fact]
    public async Task Grep_SkipsAnnotations_WhenTheyWouldPushBodyOverBudget()
    {
        // 150 个方法各一处命中：无标注约 15KB，每处再加一行标注就超 16KB
        string padding = new('x', 80);
        IEnumerable<string> methods = Enumerable.Range(0, 150).Select(i => $"    void M{i}()\n    {{\n        Hit(); // {padding}\n    }}");
        await File.WriteAllTextAsync(Path.Combine(_dir, "c.cs"), "class C\n{\n" + string.Join('\n', methods) + "\n}\n",
            TestContext.Current.CancellationToken);
        var tools = new PermissiveFileAccessTools(_dir);

        string result = await tools.Grep("Hit\\(", ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("命中过多", result);
        Assert.DoesNotContain("[位于", result);
        Assert.Contains("共 150 处命中", result);
    }
}
