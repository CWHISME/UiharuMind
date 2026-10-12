using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Artifacts;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 单聊右栏「产物」页签的显隐：<c>Conversation.Artifacts</c> 为 null（新会话、非智能体会话）时整块藏起，
/// 不能带着空 DataContext 裸奔——那时标题与说明全空、文件夹按钮点不了，只剩静态空文案。
/// 判空守卫必须挂在继承页面 DataContext 的父级（见 AgentSidePanel.axaml），不能与
/// <c>DataContext="{Binding Conversation.Artifacts}"</c> 写在同一元素上（同元素绑定按新的
/// DataContext 求值，空时守卫恒为真）。本测试按修好的结构（守卫在父级）钉住两端行为。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ArtifactsPanelVisibilityTests
{
    private sealed class EmptySource : IArtifactSource
    {
        public string DraftRoom => "/tmp/uiharu-artifacts-visibility-nonexistent";
        public string Title => "T";
        public string Hint => "H";
        public IReadOnlyList<GroupArtifact> Collect() => [];
        public bool IsRelevant(string sessionId) => false;
    }

    private sealed class StubPage : INotifyPropertyChanged
    {
        private ArtifactsViewData? _artifacts;
        public ArtifactsViewData? Artifacts
        {
            get => _artifacts;
            set { _artifacts = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Artifacts))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static GroupArtifact Draft(string name) =>
        new($"/tmp/probe/draft/{name}", name, EGroupArtifactSource.DraftRoom, [], DateTimeOffset.Now);

    private static GroupArtifact InWorkspace(string name) =>
        new($"/tmp/probe/ws/{name}", name, EGroupArtifactSource.Workspace, ["作者"], DateTimeOffset.Now);

    private sealed class FixedSource(IReadOnlyList<GroupArtifact> items) : IArtifactSource
    {
        public string DraftRoom => "/tmp/uiharu-artifacts-visibility-nonexistent";
        public string Title => "T";
        public string Hint => "H";
        public IReadOnlyList<GroupArtifact> Collect() => items;
        public bool IsRelevant(string sessionId) => false;
    }

    [Fact]
    public void NullArtifacts_HidesWholeBlock()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, StubPage stub, ScrollViewer scroll, ArtifactsPanel panel) = Build();
            try
            {
                stub.Artifacts = null;
                Pump(window);
                Assert.False(scroll.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EmptyArtifacts_ShowsTitleHintEmptyTextAndWorkingFolderButton()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, StubPage stub, ScrollViewer scroll, ArtifactsPanel panel) = Build();
            try
            {
                using ArtifactsViewData data = new(new EmptySource());
                stub.Artifacts = data;
                Pump(window);

                Assert.True(scroll.IsVisible);
                Assert.Same(data, panel.DataContext);
                List<string> texts = panel.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(t => t.Text ?? string.Empty)
                    .ToList();
                Assert.Contains("T", texts);
                Assert.Contains("H", texts);
                Button folder = panel.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(b => b is ThemedIconButton and not ThemedIconToggleButton);
                Assert.NotNull(folder.Command);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>只看草稿：草稿行留、改工作区的行藏，实例不重建，切回去原样回来</summary>
    [Fact]
    public void DraftOnly_HidesWorkspaceKeepsDrafts()
    {
        HeadlessUi.Run(() =>
        {
            using ArtifactsViewData data = new(new FixedSource([Draft("a.md"), InWorkspace("b.cs")]));
            ArtifactsPanel panel = new() { DataContext = data, Width = 260 };
            Window window = new() { Width = 300, Height = 500, Content = panel };
            try
            {
                window.Show();
                ArtifactListSync.Apply(data.Items, [Draft("a.md"), InWorkspace("b.cs")]);
                data.DraftOnly = true;
                Pump(window);

                Assert.Equal(2, data.Items.Count);
                ArtifactItem draft = data.Items.First(x => x.IsDraft);
                ArtifactItem workspace = data.Items.First(x => !x.IsDraft);
                Assert.True(draft.MatchesFilter);
                Assert.False(workspace.MatchesFilter);
                Assert.False(data.IsEmpty);
                Assert.False(data.IsDraftEmpty);

                Dictionary<string, bool> rowVisible = panel.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(b => b.DataContext is ArtifactItem)
                    .ToDictionary(b => ((ArtifactItem)b.DataContext!).Name, b => b.IsVisible);
                Assert.True(rowVisible["a.md"]);
                Assert.False(rowVisible["b.cs"]);

                data.DraftOnly = false;
                Pump(window);
                Assert.Same(draft, data.Items.First(x => x.IsDraft));
                Assert.True(data.Items.All(x => x.MatchesFilter));
                Assert.False(data.IsDraftEmpty);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>只看草稿且草稿为空：报草稿空而不是盘空，悬停说明跟着开关翻</summary>
    [Fact]
    public void DraftOnly_WithNoDrafts_ReportsDraftEmpty()
    {
        HeadlessUi.Run(() =>
        {
            using ArtifactsViewData data = new(new FixedSource([InWorkspace("b.cs")]));
            ArtifactsPanel panel = new() { DataContext = data, Width = 260 };
            Window window = new() { Width = 300, Height = 500, Content = panel };
            try
            {
                window.Show();
                ArtifactListSync.Apply(data.Items, [InWorkspace("b.cs")]);
                ThemedIconToggleButton toggle = panel.GetVisualDescendants().OfType<ThemedIconToggleButton>().Single();
                string tipAll = data.FilterTip;

                data.DraftOnly = true;
                Pump(window);

                Assert.False(data.IsEmpty);
                Assert.True(data.IsDraftEmpty);
                Assert.False(data.Items.Single().MatchesFilter);
                Assert.NotEqual(tipAll, data.FilterTip);
                Assert.Equal(data.FilterTip, ToolTip.GetTip(toggle));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>行 tooltip 停靠在行左侧：不压住光标、不抢滚轮（默认跟指针会挡住列表滚动）</summary>
    [Fact]
    public void RowTooltip_ParksBesideRow()
    {
        HeadlessUi.Run(() =>
        {
            using ArtifactsViewData data = new(new FixedSource([Draft("a.md")]));
            ArtifactsPanel panel = new() { DataContext = data, Width = 260 };
            Window window = new() { Width = 300, Height = 500, Content = panel };
            try
            {
                window.Show();
                ArtifactListSync.Apply(data.Items, [Draft("a.md")]);
                Pump(window);
                Button row = panel.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(b => b.DataContext is ArtifactItem);
                Assert.Equal(PlacementMode.Left, ToolTip.GetPlacement(row));
            }
            finally
            {
                window.Close();
            }
        });
    }

    // 与 AgentSidePanel.axaml 修好的结构同形：守卫在父级 ScrollViewer 上，DataContext 在面板上
    private static (Window, StubPage, ScrollViewer, ArtifactsPanel) Build()
    {
        StubPage stub = new();
        ArtifactsPanel panel = new() { Width = 260 };
        panel.Bind(Control.DataContextProperty, new Binding("Artifacts"));
        ScrollViewer scroll = new() { Content = panel };
        scroll.Bind(Visual.IsVisibleProperty, new Binding("Artifacts")
        {
            Mode = BindingMode.OneWay,
            Converter = ObjectConverters.IsNotNull,
        });
        StackPanel host = new() { DataContext = stub };
        host.Children.Add(scroll);
        Window window = new() { Width = 300, Height = 400, Content = host };
        window.Show();
        Pump(window);
        return (window, stub, scroll, panel);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }
}
