using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 快照器是模型看网页的唯一窗口：编号给谁、哪些行省掉、哪些收成一行，直接决定 token 与点不点得中
/// </summary>
public class AxSnapshotWriterTests
{
    private static AxNode Node(string role, string name = "", params AxNode[] children) =>
        new(role, name) { Children = children };

    private static AxNode Text(string text) => new("StaticText", text);

    private static string Normalize(string text) => text.ReplaceLineEndings("\n");

    [Fact]
    public void OnlyActionableElements_GetRefs_InDocumentOrder()
    {
        AxNode root = Node("RootWebArea", "Sign in",
            Node("heading", "Welcome", Text("Welcome")) with { Level = 1 },
            Node("textbox", "Email") with { Value = "a@b.c" },
            Node("button", "Submit", Text("Submit")));

        AxSnapshot snapshot = AxSnapshotWriter.Write(root);

        Assert.Equal(Normalize(
            """
            - heading "Welcome" [level=1]
            - textbox "Email" [ref=e1]: a@b.c
            - button "Submit" [ref=e2]
            """), Normalize(snapshot.Text));
        Assert.Equal("Email", snapshot.Refs["e1"].Name);
        Assert.Equal("Submit", snapshot.Refs["e2"].Name);
    }

    /// <summary>没名字的图标按钮、链接带上描述或去处，有名字的不重复给</summary>
    [Fact]
    public void UnnamedActionableElements_ShowTheirDescriptionOrUrl()
    {
        AxNode root = Node("RootWebArea", "Profile",
            Node("button") with { Description = "Show more activity" },
            Node("link") with { Url = "https://jira.example.com/activity?rss" },
            Node("link", "Home") with { Url = "https://jira.example.com/" });

        AxSnapshot snapshot = AxSnapshotWriter.Write(root);

        Assert.Equal(Normalize(
            """
            - button [description="Show more activity"] [ref=e1]
            - link [url=https://jira.example.com/activity?rss] [ref=e2]
            - link "Home" [ref=e3]
            """), Normalize(snapshot.Text));
    }

    /// <summary>无名容器不成行；只装着一段字的段落、列表项收成一行；链接里重复自己名字的那段字省掉</summary>
    [Fact]
    public void UnnamedContainers_AreFlattened_AndLoneTextIsInlined()
    {
        AxNode root = Node("RootWebArea", "",
            Node("generic", "",
                Node("paragraph", "", Text("Hello world")),
                Node("list", "",
                    Node("listitem", "", Node("link", "Docs", Node("generic", "", Text("Docs")))),
                    Node("listitem", "", Text("Plain item")))));

        Assert.Equal(Normalize(
            """
            - paragraph: Hello world
            - list
              - listitem
                - link "Docs" [ref=e1]
              - listitem: Plain item
            """), Normalize(AxSnapshotWriter.Write(root).Text));
    }

    [Fact]
    public void States_AreShown_AndDisabledElementsGetNoRef()
    {
        AxNode root = Node("RootWebArea", "",
            Node("checkbox", "Remember me") with { Checked = "true" },
            Node("combobox", "Size") with { Expanded = false, Value = "Medium" },
            Node("button", "Pay") with { Disabled = true });

        AxSnapshot snapshot = AxSnapshotWriter.Write(root);

        Assert.Equal(Normalize(
            """
            - checkbox "Remember me" [checked] [ref=e1]
            - combobox "Size" [collapsed] [ref=e2]: Medium
            - button "Pay" [disabled]
            """), Normalize(snapshot.Text));
        Assert.Equal(2, snapshot.Refs.Count);
    }

    /// <summary>
    /// 精简版把导航、页脚收成一行，正文照旧；编号两版一致——在完整版里找到的导航链接照样点得着
    /// </summary>
    [Fact]
    public void CompactText_CollapsesNavigationAndFooter_KeepingRefs()
    {
        AxNode root = Node("RootWebArea", "",
            Node("navigation", "Main menu",
                Node("link", "Home"),
                Node("link", "About")),
            Node("main", "",
                Node("heading", "Article") with { Level = 1 },
                Node("button", "Like")),
            Node("contentinfo", "",
                Node("link", "Privacy")));

        AxSnapshot snapshot = AxSnapshotWriter.Write(root);

        Assert.Equal(Normalize(
            """
            - navigation "Main menu"
              - … 2 lines collapsed
            - main
              - heading "Article" [level=1]
              - button "Like" [ref=e3]
            - contentinfo
              - … 1 lines collapsed
            """), Normalize(snapshot.CompactText));
        Assert.Contains("- link \"About\" [ref=e2]", snapshot.Text);
        Assert.Equal("Privacy", snapshot.Refs["e4"].Name);
    }

    /// <summary>表单标签那段字紧挨着同名输入框时只留输入框：两行说的是同一件事</summary>
    [Fact]
    public void LabelTextNextToItsInput_IsDropped()
    {
        AxNode root = Node("RootWebArea", "",
            Node("form", "",
                Text("Customer name:"),
                Node("textbox", "Customer name:"),
                Node("checkbox", "Bacon"),
                Text("Bacon"),
                Text("Pizza Size")));

        Assert.Equal(Normalize(
            """
            - form
              - textbox "Customer name:" [ref=e1]
              - checkbox "Bacon" [ref=e2]
              - text: Pizza Size
            """), Normalize(AxSnapshotWriter.Write(root).Text));
    }

    /// <summary>收起的原生下拉框里的选项没有位置、点不着：只列出来不编号，模型用 select 按文字选</summary>
    [Fact]
    public void OptionsInsideAClosedDropDown_GetNoRefs_ButKnowTheirDropDown()
    {
        AxNode large = Node("option", "Large");
        AxNode size = Node("combobox", "Size",
            Node("MenuListPopup", "",
                Node("option", "Small") with { Selected = true },
                large)) with { Expanded = false, Value = "Small" };
        AxNode root = Node("RootWebArea", "", size);

        AxSnapshot snapshot = AxSnapshotWriter.Write(root);

        Assert.Same(size, snapshot.PickerOptions[large]);

        Assert.Equal(Normalize(
            """
            - combobox "Size" [collapsed] [ref=e1]: Small
              - option "Small" [selected]
              - option "Large"
            """), Normalize(snapshot.Text));
        Assert.Single(snapshot.Refs);
    }

    /// <summary>div 做的按钮没有编号，只能按字找：整句相等优先，可操作的元素优先，页面标题不算</summary>
    [Fact]
    public void FindByText_PrefersActionableExactMatches()
    {
        AxNode button = Node("button", "登录");
        AxNode root = Node("RootWebArea", "登录",
            Node("heading", "登录") with { Level = 1 },
            Node("generic", "", button));

        Assert.Same(button, AxSnapshotSearch.FindByText(root, " 登录 "));
    }

    [Fact]
    public void FindByText_FallsBackToContains()
    {
        AxNode comments = Text("查看全部 12 条评论");
        AxNode root = Node("RootWebArea", "评论区", Node("generic", "", comments));

        Assert.Same(comments, AxSnapshotSearch.FindByText(root, "全部 12 条"));
        Assert.Null(AxSnapshotSearch.FindByText(root, "评论区"));
    }

    /// <summary>快照里写的是 [ref=e5]，模型常照抄成 ref=e5 或带着方括号：都认成编号；看得见的文字原样</summary>
    [Theory]
    [InlineData("e5", "e5")]
    [InlineData(" ref=e5 ", "e5")]
    [InlineData("[ref=e12]", "e12")]
    [InlineData("E7", "e7")]
    [InlineData("Submit order", "Submit order")]
    [InlineData("email", "email")]
    public void Targets_AreNormalizedToRefs(string target, string expected)
    {
        Assert.Equal(expected, AxSnapshotSearch.NormalizeTarget(target));
    }

    /// <summary>密码框标出来、值只说填没填——Chrome 本就给圆点，换别的引擎也不透出长度</summary>
    [Fact]
    public void PasswordFields_AreMarked_AndTheirValueIsMasked()
    {
        AxNode root = Node("RootWebArea", "Login",
            Node("textbox", "Username") with { Value = "tomsmith" },
            Node("textbox", "Password") with { Value = "hunter2hunter2", Secret = true });

        AxSnapshot snapshot = AxSnapshotWriter.Write(root);

        Assert.Equal(Normalize(
            """
            - textbox "Username" [ref=e1]: tomsmith
            - textbox "Password" [password] [ref=e2]: ••••••
            """), Normalize(snapshot.Text));
    }

    /// <summary>正文的段落之间隔着空行：上下文取最近的非空行，只隔着空行的不算断开</summary>
    [Fact]
    public void FindLines_SkipsBlankLinesForContext()
    {
        string text = "# Title\n\nFirst paragraph.\n\nKuroko teleports.\n\nLast paragraph.\n\nUnrelated.";

        Assert.Equal("First paragraph.\nKuroko teleports.\nLast paragraph.",
            Normalize(AxSnapshotSearch.FindLines(text, "kuroko")));
        Assert.Equal("Kuroko teleports.", Normalize(AxSnapshotSearch.FindLines(text, "kuroko", withContext: false)));
    }

    [Fact]
    public void FindLines_KeepsMatchesWithOneLineOfContext()
    {
        string snapshot = "- a\n- b\n- target one\n- c\n- d\n- e\n- Target two";

        Assert.Equal("- b\n- target one\n- c\n…\n- e\n- Target two",
            Normalize(AxSnapshotSearch.FindLines(snapshot, "target")));
        Assert.Equal("(no line contains \"zzz\")", AxSnapshotSearch.FindLines(snapshot, "zzz"));
    }
}
