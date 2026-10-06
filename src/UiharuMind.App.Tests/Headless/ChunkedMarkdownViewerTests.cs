using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        foreach (SimpleMarkdownViewer chunk in chunks) chunk.RealizeNow();
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
        chunks[0].RealizeNow();
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
        chunks[1].RealizeNow();
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

    private static void Settle(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }
    }

    /// <summary>垫片靠反射取 LiveMarkdown 的私有成员。升级库后这里红了，说明垫片已静默失效：先看上游修没修，修了就删垫片</summary>
    [Fact]
    public void SelectionSnapshotShim_LibraryInternalsStillPresent()
    {
        Assert.True(MarkdownSelectionSnapshotShim.IsAvailable);
    }
}
