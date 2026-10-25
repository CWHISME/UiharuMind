using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Artifacts;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 产物区的几何：标题紧贴列表、空状态整块收起时不占位、压矮高度时列表照样能滚。
///
/// 起因是原先用 <c>Grid RowDefinitions="Auto,Auto,Auto,Auto,*"</c> 加 <c>RowSpacing="8"</c>：
/// RowSpacing 连高度为 0 的空状态行也算，三段隐藏文案在标题和列表之间留下 24px 白洞。
/// 现在改成 DockPanel（标题 Top、列表填满），空状态自己整块收起。这里钉住量到的两个口径。
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ArtifactsPanelLayoutTests
{
    private const double MaxTitleToListGap = 12; //标题文字底边到列表顶边：一行 RowSpacing 的余量
    private const string PanelTitle = "本会话产物";
    private const string HintText = "agent 落的盘都在这儿。点开看文件，右键在文件夹中显示。";

    private sealed class FixedSource(IReadOnlyList<GroupArtifact> items) : IArtifactSource
    {
        public string DraftRoom => Path.Combine(Path.GetTempPath(), "uiharu-artifacts-layout-nonexistent");
        public string Title => PanelTitle;
        public string Hint => HintText;
        public IReadOnlyList<GroupArtifact> Collect() => items;
        public bool IsRelevant(string sessionId) => false;
    }

    /// <summary>有产物时标题下方只留一行间距，不许再出现空状态的白洞</summary>
    [Fact]
    public void FilledPanel_TitleSitsRightAboveList()
    {
        HeadlessUi.Run(() =>
        {
            using ArtifactsViewData data = new(new FixedSource(Artifacts()));
            ArtifactsPanel panel = new() { DataContext = data, Width = 260 };
            Window window = new() { Width = 300, Height = 500, Content = panel };
            try
            {
                window.Show();
                ArtifactListSync.Apply(data.Items, Artifacts());
                PumpUntil(window, () => !data.IsEmpty);

                double gap = ListTop(panel) - TitleBottom(panel);
                Assert.InRange(gap, 0, MaxTitleToListGap);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>盘上一条没有：说明与空文案都露出来，且不被面板底边裁掉</summary>
    [Fact]
    public void EmptyPanel_ShowsNoticeFully()
    {
        HeadlessUi.Run(() =>
        {
            using ArtifactsViewData data = new(new FixedSource([]));
            ArtifactsPanel panel = new() { DataContext = data, Width = 260 };
            Window window = new() { Width = 300, Height = 300, Content = panel };
            try
            {
                window.Show();
                Pump(window); //空源刷新后 IsEmpty 仍是 true，初始状态与刷新后一致，不必等
                Assert.True(data.IsEmpty);
                Assert.True(data.HasEmptyNotice);

                TextBlock hint = panel.GetVisualDescendants().OfType<TextBlock>()
                    .First(t => t.Text == HintText);
                Assert.True(hint.IsVisible);
                Point hintTop = hint.TranslatePoint(default, panel) ?? default;
                Assert.InRange(hintTop.Y + hint.Bounds.Height, 0, panel.Bounds.Height);
                Assert.True(ListTop(panel) > hintTop.Y + hint.Bounds.Height);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>压到 220 高：列表拿到受限高度、内容溢出可滚（虚拟化没被 DockPanel 换成无限高）</summary>
    [Fact]
    public void TightHeight_ListStillScrolls()
    {
        HeadlessUi.Run(() =>
        {
            using ArtifactsViewData data = new(new FixedSource(Artifacts()));
            ArtifactsPanel panel = new() { DataContext = data, Width = 260 };
            Window window = new() { Width = 300, Height = 220, Content = panel };
            try
            {
                window.Show();
                ArtifactListSync.Apply(data.Items, Artifacts());
                PumpUntil(window, () => !data.IsEmpty);

                ScrollViewer scroller = panel.GetVisualDescendants().OfType<ListBox>().Single()
                    .GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(scroller.Viewport.Height > 0);
                Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                    $"内容没溢出就量不到滚动：extent {scroller.Extent.Height} viewport {scroller.Viewport.Height}");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static List<GroupArtifact> Artifacts() =>
    [
        Artifact(@"3dspike\shots\r5-dbg4\look-1.png", EGroupArtifactSource.Workspace, ["绿酱"]),
        Artifact("strip.png", EGroupArtifactSource.DraftRoom, []),
        Artifact(@"images\ba4332f2064f4082.png", EGroupArtifactSource.DraftRoom, []),
        Artifact(@"3dspike\shots\r5-dbg3\look-2.png", EGroupArtifactSource.Workspace, ["绿酱"]),
    ];

    private static GroupArtifact Artifact(string displayPath, EGroupArtifactSource source, IReadOnlyList<string> authors) =>
        new(Path.Combine(Path.GetTempPath(), "uiharu-artifacts-layout", displayPath), displayPath, source, authors,
            DateTimeOffset.Now);

    private static double TitleBottom(ArtifactsPanel panel)
    {
        TextBlock title = panel.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == PanelTitle);
        return (title.TranslatePoint(default, panel) ?? default).Y + title.Bounds.Height;
    }

    private static double ListTop(ArtifactsPanel panel) =>
        (panel.GetVisualDescendants().OfType<ListBox>().Single().TranslatePoint(default, panel) ?? default).Y;

    // 产物刷新的防抖在后台（400ms）；等结果落地再看布局，否则量到的是初始的空状态
    private static void PumpUntil(Window window, Func<bool> done)
    {
        for (int i = 0; i < 30 && !done(); i++)
        {
            Thread.Sleep(50);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        Pump(window);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
