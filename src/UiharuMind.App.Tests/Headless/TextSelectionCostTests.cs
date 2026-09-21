using Avalonia.Controls;
using LiveMarkdown.Avalonia;
using Avalonia.Media;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 拖选一段长文本的<b>每帧代价</b>。
///
/// 起因是一次真实事故：子代理弹窗里选了一段工具卡的文字，界面卡了一下，进程
/// phys_footprint 冲到 7.8GB——而同期活对象始终只有 206MB，全是「生出来立刻就死」的整形垃圾。
/// 成因在 Avalonia 12.1.2 <c>SelectableTextBlock</c>：选区一变就 <c>InvalidateTextLayout()</c>，
/// 连字形整形缓存一起扔——而选区只影响前景色，整形结果一个字都没变。
///
/// 这里记的是<b>当前实测值</b>，不是验收线。上游修掉之后基线那一档会自己掉下来，
/// 那时该做的是回来把结论连同基类一起删掉，而不是把断言放宽。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class TextSelectionCostTests(ITestOutputHelper output)
{
    private const int DragSteps = 100; //一次两三秒的拖选大致是两三百个指针事件,这里取个整数好换算
    private const int LongTextChars = 32 * 1024; //一条长回复的量级

    /// <param name="chars">文本长度</param>
    /// <returns>一段有换行的长文本</returns>
    private static string LongText(int chars)
    {
        System.Text.StringBuilder builder = new(chars + 64);
        int line = 0;
        while (builder.Length < chars) builder.Append($"第 {line++} 行：这是一段用来量整形代价的正文内容。\n");
        return builder.ToString();
    }

    /// <summary>
    /// 在滚动容器里放一块长文本，然后模拟拖选：每步改一次选区终点，再走一趟排版。
    /// 必须套 <see cref="ScrollViewer"/>——直接塞进窗口会被窗口高度约束住，量不到真实的整形量
    /// </summary>
    /// <param name="block">受测文本块</param>
    /// <param name="text">块内文本</param>
    /// <param name="steps">拖选步数</param>
    /// <returns>拖选 <paramref name="steps"/> 步的总分配字节数</returns>
    private static long MeasureDrag(SelectableTextBlock block, string text, int steps = DragSteps)
    {
        block.Text = text;
        block.TextWrapping = TextWrapping.Wrap;
        Window window = new() { Width = 900, Height = 700, Content = new ScrollViewer { Content = block } };
        window.Show();
        window.UpdateLayout();

        // 预热:第一次整形的成本不该算在拖选头上
        block.SelectionStart = 0;
        block.SelectionEnd = 1;
        window.UpdateLayout();
        _ = block.TextLayout.TextLines.Count;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i <= steps; i++)
        {
            block.SelectionEnd = text.Length * i / steps;
            window.UpdateLayout();
            _ = block.TextLayout.TextLines.Count; //排版是懒建的,渲染时才真正被逼出来
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        window.Close();
        return allocated;
    }

    private sealed class PlainBlock : SelectableTextBlock; //上游原样,作对照基线

    /// <summary>
    /// 上游的拖选代价随<b>整块文本长度</b>涨，而不是随选中了多少字涨——这是「重新整形整块」
    /// 而非「重画选区」的指纹。选区其实只影响前景色，整形结果一个字都没变。
    /// </summary>
    [Fact]
    public void UpstreamDragSelection_CostScalesWithBlockLength() => HeadlessUi.Run(() =>
    {
        long small = MeasureDrag(new PlainBlock(), LongText(2 * 1024));
        long large = MeasureDrag(new PlainBlock(), LongText(LongTextChars));

        output.WriteLine($"上游 2KB 正文,拖选 {DragSteps} 步:{small / 1048576.0:F1} MB");
        output.WriteLine($"上游 32KB 正文,拖选 {DragSteps} 步:{large / 1048576.0:F1} MB");

        Assert.True(large > small * 4,
            $"拖选代价不再随文本长度放大(2KB {small}B vs 32KB {large}B),上游可能已经修了");
    });

    /// <summary>
    /// 正文气泡走的是 <c>LiveMarkdown.Avalonia</c> 的 <see cref="MarkdownTextBlock"/>，
    /// 它继承同一个上游缺陷，而且还叠了一个自己的：覆盖 <c>CreateTextLayout</c> 时
    /// <b>没有把 <c>TextRunCache</c> 传给 <c>TextLayout</c></b>（Avalonia 原版
    /// <c>TextBlock.CreateTextLayout</c> 是传的）。于是这个控件从来就没有过整形缓存，
    /// 而 <c>TextBlock.ArrangeOverride</c> 每趟都无条件重建 <c>TextLayout</c>，
    /// 次次从零整形整块文本。
    ///
    /// 曾经在 fork 里把缓存补上过，实测 23,748MB → 129MB（184×），但连同另一处改动一起
    /// 撤回了——用户实际使用中出现流式输出中断与渲染异常，而无头里复现不出来、归因不了。
    /// 详见 docs/adr/0039。**这条测试留着是为了记住这笔账**：数字在这儿，
    /// 哪天要重做（提 PR 给上游，或再开 fork）不必从头量一遍。
    /// </summary>
    [Fact]
    public void MarkdownTextBlock_IsEvenWorse_AndWeCannotReachIt() => HeadlessUi.Run(() =>
    {
        // 样本压小:同样的比值,但不必让一条测试自己分配二十多个 G
        const int steps = 20;
        string text = LongText(8 * 1024);
        long plain = MeasureDrag(new PlainBlock(), text, steps);
        long markdown = MeasureDrag(new MarkdownTextBlock(), text, steps);

        output.WriteLine($"上游 SelectableTextBlock:{plain / 1048576.0:F0} MB");
        output.WriteLine($"MarkdownTextBlock:{markdown / 1048576.0:F0} MB（{(double)markdown / plain:F0}×）");

        Assert.True(markdown > plain,
            $"MarkdownTextBlock 不再更贵({plain}B vs {markdown}B)——上游多半已经把 TextRunCache 传上了,"
            + "那本条与 docs/adr/0039 的结论都该更新");
    });
}
