using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Input.Raw;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 长文档预览打开时只渲染看得见的段。整篇一个渲染器时 100K 字要建两千多个控件、两三秒，
/// 分段后打开只建视口里那一两段
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ChunkedMarkdownViewerTests
{
    [Fact]
    public void Open_RendersOnlyVisibleChunks() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 400).Select(i => $"## 标题 {i}\n\n第 {i} 段正文，带 `code` 与 **粗体**，再多写几个字凑长度。\n\n"));
        ChunkedMarkdownViewer viewer = new();
        Window window = new() { Width = 900, Height = 700, Content = new ScrollViewer { Content = viewer } };
        window.Show();

        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        List<SimpleMarkdownViewer> chunks = viewer.GetVisualDescendants().OfType<SimpleMarkdownViewer>().ToList();
        int rendered = chunks.Count(c => c.GetVisualDescendants().OfType<MarkdownRenderer>().Any(r => r.IsVisible));
        Assert.True(chunks.Count > 3, $"只切出 {chunks.Count} 段");
        Assert.InRange(rendered, 1, 2);
        window.Close();
    });

    /// <summary>
    /// 离视口远的段连纯文本占位都不排：整篇排字量高度正是 200K 文档打开卡一两秒的原因
    /// </summary>
    [Fact]
    public void Open_FarChunksStayOutOfLayout() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 2000).Select(i => $"第 {i} 段正文，写长一点凑够字数，好让文档切成很多段。\n\n"));
        ChunkedMarkdownViewer viewer = new();
        Window window = new() { Width = 900, Height = 700, Content = new ScrollViewer { Content = viewer } };
        window.Show();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        Settle(window);

        List<SimpleMarkdownViewer> chunks = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();
        int laidOut = chunks.Count(c => c.IsVisible);
        Assert.True(chunks.Count > 10, $"只切出 {chunks.Count} 段");
        Assert.InRange(laidOut, 1, 3);
        Assert.True(viewer.Bounds.Height > window.Height * 5, "估高占位没把滚动区撑开");
        window.Close();
    });

    /// <summary>
    /// 文本窗里预览居中限宽：未放出的段不能缩成零宽，否则永远判不进视口、整片空白
    /// </summary>
    [Fact]
    public void CenteredLayout_StillReleasesVisibleChunks() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 2000).Select(i => $"第 {i} 段正文，写长一点凑够字数，好让文档切成很多段。\n\n"));
        ChunkedMarkdownViewer viewer = new() { MaxWidth = 900, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        ScrollViewer scroll = new() { Content = viewer };
        Window window = new() { Width = 1200, Height = 700, Content = scroll };
        window.Show();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        Settle(window);
        Assert.Contains(viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>(), c => c.IsVisible);

        scroll.Offset = new Vector(0, viewer.Bounds.Height / 2);
        Settle(window);
        List<SimpleMarkdownViewer> chunks = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();
        Assert.Contains(chunks.Skip(chunks.Count / 4), c => c.IsVisible);
        window.Close();
    });

    /// <summary>
    /// 放出的段直接是排版后的样子，不先露一帧原文再跳（同聊天气泡的口径）
    /// </summary>
    [Fact]
    public void ReleasedChunks_SkipRawPlaceholder() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 2000).Select(i => $"## 第 {i} 节\n\n正文带 **粗体**，写长一点凑够字数。\n\n"));
        ChunkedMarkdownViewer viewer = new();
        ScrollViewer scroll = new() { Content = viewer };
        Window window = new() { Width = 900, Height = 700, Content = scroll };
        window.Show();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        window.UpdateLayout();

        List<SimpleMarkdownViewer> shown = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().Where(c => c.IsVisible).ToList();
        Assert.NotEmpty(shown);
        Assert.All(shown, c => Assert.True(c.IsMarkdownRealized));
        window.Close();
    });

    /// <summary>
    /// 从一段拖到下一段，两段的文字块都要被选中——各段是独立渲染器，靠共享选区作用域衔接
    /// </summary>
    [Fact]
    public void DragAcrossChunkBoundary_SelectsBothChunks() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 200).Select(i => $"第 {i} 段正文，写长一点凑够字数，好让文档切成好几段。\n\n"));
        ChunkedMarkdownViewer viewer = new();
        Window window = new() { Width = 900, Height = 3000, Content = viewer };
        window.Show();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        List<SimpleMarkdownViewer> chunks = viewer.GetVisualDescendants().OfType<SimpleMarkdownViewer>().ToList();
        foreach (SimpleMarkdownViewer chunk in chunks) RealizeNow(chunk);
        window.UpdateLayout();
        Assert.True(chunks.Count >= 2, $"只切出 {chunks.Count} 段");

        MarkdownTextBlock last = chunks[0].GetVisualDescendants().OfType<MarkdownTextBlock>().Last();
        MarkdownTextBlock first = chunks[1].GetVisualDescendants().OfType<MarkdownTextBlock>().First();
        Point from = last.TranslatePoint(new Point(5, last.Bounds.Height / 2), window)!.Value;
        Point to = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window)!.Value;

        window.MouseDown(from, MouseButton.Left);
        for (int i = 1; i <= 10; i++) window.MouseMove(from + (to - from) * (i / 10.0));
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(last.SelectionStart, last.SelectionEnd);
        Assert.NotEqual(first.SelectionStart, first.SelectionEnd);
        window.Close();
    });

    /// <summary>
    /// 拖到一半下一段才渲染出来（拖着往下滚时就是这样），继续拖进去也要接得上。
    /// 库只在按下时取一次块快照，靠 <see cref="MarkdownSelectionSnapshotShim"/> 补
    /// </summary>
    [Fact]
    public void ChunkRealizedMidDrag_JoinsSelection() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 200).Select(i => $"第 {i} 段正文，写长一点凑够字数，好让文档切成好几段。\n\n"));
        ChunkedMarkdownViewer viewer = new();
        ScrollViewer scroll = new() { Content = viewer };
        Window window = new() { Width = 900, Height = 600, Content = scroll };
        window.Show();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        Settle(window); //先让各段 Loaded、收到视口通知，否则没收到通知的段会走兜底直接渲染

        // 第一段渲染好，把分界线停在视口底下一点：第二段此刻在视口外，队列不会去渲染它
        List<SimpleMarkdownViewer> chunks = viewer.GetVisualDescendants().OfType<SimpleMarkdownViewer>().ToList();
        Assert.True(chunks.Count >= 2, $"只切出 {chunks.Count} 段");
        RealizeNow(chunks[0]);
        window.UpdateLayout();
        scroll.Offset = new Vector(0, chunks[1].Bounds.Y - window.Height - 20);
        Settle(window);
        Assert.False(chunks[1].IsMarkdownRealized);

        MarkdownTextBlock last = chunks[0].GetVisualDescendants().OfType<MarkdownTextBlock>()
            .Last(b => b.TranslatePoint(new Point(0, b.Bounds.Height / 2), window)!.Value.Y < window.Height - 5);
        Point from = last.TranslatePoint(new Point(5, last.Bounds.Height / 2), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(30, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(from + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
        Assert.False(chunks[1].IsMarkdownRealized);

        // 拖动中途第二段渲染出来，再继续拖进去
        RealizeNow(chunks[1]);
        window.UpdateLayout();
        MarkdownTextBlock first = chunks[1].GetVisualDescendants().OfType<MarkdownTextBlock>().First();
        Point to = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window)!.Value;
        for (int i = 1; i <= 10; i++) window.MouseMove(from + (to - from) * (i / 10.0), RawInputModifiers.LeftMouseButton);

        // 松手前断言：松手那一下库会再按落点补一次选区，会把拖动途中的断档盖住
        Assert.NotEqual(last.SelectionStart, last.SelectionEnd);
        Assert.NotEqual(first.SelectionStart, first.SelectionEnd);
        window.MouseUp(to, MouseButton.Left);
        window.Close();
    });

    // 段先放出（离视口远的段不参与布局）再渲染
    private static void RealizeNow(SimpleMarkdownViewer chunk)
    {
        ((MarkdownChunkSlot)chunk.Parent!).Activate();
        chunk.RealizeNow();
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span)
                foreach (Run nested in EnumerateRuns(span.Inlines))
                    yield return nested;
        }
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }
    }

    /// <summary>
    /// 预解析失败的段降级显示纯文本，而不是藏起原文又给出空白渲染器
    /// （千层嵌套引用：切块走 SplitPlain 兜底，单段种子解析照样抛）
    /// </summary>
    [Fact]
    public void SeedFailedChunk_FallsBackToPlaintext() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Repeat("> ", 1000)) + "x\n"
            + string.Concat(Enumerable.Range(0, 400).Select(i => $"第 {i} 段正文凑长度。\n\n"));
        ChunkedMarkdownViewer viewer = new();
        Window window = new() { Width = 900, Height = 600, Content = new ScrollViewer { Content = viewer } };
        window.Show();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        Settle(window);

        List<SimpleMarkdownViewer> chunks = viewer.GetVisualDescendants().OfType<SimpleMarkdownViewer>().ToList();
        Assert.True(chunks.Count > 1, $"只切出 {chunks.Count} 段");
        SimpleMarkdownViewer first = chunks[0];
        Assert.True(first.IsMarkdownRealized);
        SelectableTextBlock plain = first.FindControl<SelectableTextBlock>("PlainTextBlock")!;
        Assert.NotNull(plain);
        Assert.True(plain.IsVisible);
        Assert.False(first.Renderer.IsVisible);
        window.Close();
    });

    /// <summary>
    /// 流式追加中的 [名字]: 行不能被当链接定义吞掉（增量追加平时 raw 拼，
    /// 含 ]: 的 delta 才走全量转义）
    /// </summary>
    [Fact]
    public void Append_DefinitionLikeLine_IsRenderedNotSwallowed() => HeadlessUi.Run(() =>
    {
        ChunkedMarkdownViewer viewer = new();
        Window window = new() { Width = 900, Height = 600, Content = new ScrollViewer { Content = viewer } };
        window.Show();
        viewer.MarkdownText = "开头一段。\n\n";
        viewer.IsPlaintext = false;
        viewer.AppendText("[阿笠博士]: 今天天气不错\n");
        Settle(window);

        // 库的文字块不走 Text 属性，文本在 Run 里：拼出来不断言结构
        string rendered = string.Concat(viewer.GetVisualDescendants().OfType<MarkdownTextBlock>()
            .SelectMany(b => EnumerateRuns(((TextBlock)b).Inlines))
            .Select(r => r.Text));
        Assert.Contains("阿笠博士", rendered);
        window.Close();
    });

    /// <summary>
    /// 垫片刷新永远不抛：快照非空时对着坏树刷新也只退回库原行为
    /// </summary>
    [Fact]
    public void RefreshActive_NeverThrows_OnBrokenTree() => HeadlessUi.Run(() =>
    {
        ChunkedMarkdownViewer viewer = new() { MarkdownText = "第一段。\n\n第二段。\n\n" };
        Window window = new() { Width = 900, Height = 600, Content = new ScrollViewer { Content = viewer } };
        window.Show();
        viewer.IsPlaintext = false;
        Settle(window);

        List<SimpleMarkdownViewer> chunks = viewer.GetVisualDescendants().OfType<SimpleMarkdownViewer>().ToList();
        RealizeNow(chunks[0]);
        window.UpdateLayout();
        MarkdownTextBlock block = chunks[0].GetVisualDescendants().OfType<MarkdownTextBlock>().First();
        Point p = block.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(p, MouseButton.Left);

        MarkdownSelectionSnapshotShim.RefreshActive(window, chunks.Select(c => c.Renderer).ToList());
        MarkdownSelectionSnapshotShim.RefreshActive(null!, chunks.Select(c => c.Renderer).ToList());

        window.MouseUp(p, MouseButton.Left);
        window.Close();
    });

    /// <summary>垫片靠反射取 LiveMarkdown 的私有成员。升级库后这里红了，说明垫片已静默失效：先看上游修没修，修了就删垫片</summary>
    [Fact]
    public void SelectionSnapshotShim_LibraryInternalsStillPresent()
    {
        Assert.True(MarkdownSelectionSnapshotShim.IsAvailable);
    }

    /// <summary>
    /// 流式追加（思考过程）只喂最后一段：前面的段不重建，整篇原文照样对得上
    /// </summary>
    [Fact]
    public void Append_KeepsEarlierChunks_AndTracksTheWholeText() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Range(0, 200).Select(i => $"第 {i} 段正文，写长一点凑够字数，好让文档切成好几段。\n\n"));
        ChunkedMarkdownViewer viewer = new() { MarkdownText = text };
        List<SimpleMarkdownViewer> before = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();

        viewer.AppendText("追加的一句");

        Assert.Equal(text + "追加的一句", viewer.MarkdownText);
        Assert.Equal(before, viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList());
    });

    /// <summary>从空白一点点长出来：最后一段过了两倍段长就重新切，前面切出来的段从此不再变</summary>
    [Fact]
    public void AppendingPastTwiceTheChunkSize_SplitsTheTail() => HeadlessUi.Run(() =>
    {
        ChunkedMarkdownViewer viewer = new();
        string expected = string.Empty;
        SimpleMarkdownViewer? firstAfterSplit = null; //头一次重切后的第一段：之后无论再长多少都不该换
        for (int i = 0; i < 1000; i++)
        {
            string delta = $"第 {i} 段思考，边想边写，一句一句往外冒。\n\n";
            viewer.AppendText(delta);
            expected += delta;
            List<SimpleMarkdownViewer> current = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();
            if (firstAfterSplit == null && current.Count > 1) firstAfterSplit = current[0];
        }

        List<SimpleMarkdownViewer> chunks = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();
        Assert.Equal(expected, viewer.MarkdownText);
        Assert.True(chunks.Count > 3, $"只切出 {chunks.Count} 段");
        Assert.Same(firstAfterSplit, chunks[0]);
    });

    /// <summary>
    /// 模型复读出的几十万字单段（实测思考段 49 万字、一行里十几万个「**」）：切不开就整块渲染，打开当场冻住。
    /// 超长单块要硬切成纯文本段，滚到底也只放出视口附近几段
    /// </summary>
    [Fact]
    public void OversizedLoopParagraph_OpensAsPlaintextChunks() => HeadlessUi.Run(() =>
    {
        string text = "# 正常开头\n\n先想一想 **关键线索**。\n\n" + string.Concat(Enumerable.Repeat("：**", 160_000));
        ChunkedMarkdownViewer viewer = new();
        ScrollViewer scroll = new() { Content = viewer };
        Window window = new() { Width = 900, Height = 700, Content = scroll };
        window.Show();

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        viewer.MarkdownText = text;
        viewer.IsPlaintext = false;
        Settle(window);
        scroll.ScrollToEnd();
        Settle(window);
        watch.Stop();

        List<SimpleMarkdownViewer> chunks = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();
        Assert.True(chunks.Count > 50, $"只切出 {chunks.Count} 段");
        Assert.True(chunks.Count(c => c.IsVisible) is >= 1 and <= 12,
            $"放出 {chunks.Count(c => c.IsVisible)}/{chunks.Count} 段，用时 {watch.ElapsedMilliseconds}ms，可见：" +
            string.Join(",", chunks.Select((c, i) => c.IsVisible ? i : -1).Where(i => i >= 0)));
        Assert.True(chunks[^1].IsPlaintext, "复读段不该按 markdown 渲染");
        Assert.True(watch.ElapsedMilliseconds < 1500, $"打开并滚到底用了 {watch.ElapsedMilliseconds}ms");
        window.Close();
    });

    /// <summary>流式长出一个超长单段：尾段切不开时不能每拍都重切，过了单块上限要硬切成多段</summary>
    [Fact]
    public void AppendingOversizedParagraph_EventuallySplitsIntoPlaintextChunks() => HeadlessUi.Run(() =>
    {
        ChunkedMarkdownViewer viewer = new();
        System.Text.StringBuilder expected = new();
        for (int i = 0; i < 4000; i++)
        {
            string delta = string.Concat(Enumerable.Repeat("：**", 20));
            viewer.AppendText(delta);
            expected.Append(delta);
        }

        List<SimpleMarkdownViewer> chunks = viewer.GetLogicalDescendants().OfType<SimpleMarkdownViewer>().ToList();
        Assert.Equal(expected.ToString(), viewer.MarkdownText);
        Assert.True(chunks.Count > 10, $"只切出 {chunks.Count} 段");
    });
}
