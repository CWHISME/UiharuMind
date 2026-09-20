using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 会话流的<b>真实排版</b>：模板、容器实体化与裁剪带来的收益，都在这里量。
///
/// 这一层是纯逻辑测试够不着的那一半。<c>ConversationItemWindowTrimmer</c> 的单元测试
/// 只能回答「集合被裁到几条」，回答不了「界面因此少排了多少东西」——而那才是
/// 「切回一个长会话卡半秒」的直接成因（会话流<b>没有虚拟化</b>，留多少条目就排多少条目）。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationViewLayoutTests(ITestOutputHelper output)
{
    private const double WindowWidth = 900;
    private const double WindowHeight = 700;

    /// <summary>
    /// 造一屏混合条目：一次真实的助手回复就是「思考段 + 正文 + 若干工具卡」，
    /// 只堆纯文本气泡量不出工具卡那一份排版成本
    /// </summary>
    /// <param name="count">条目数</param>
    /// <returns>条目列表</returns>
    private static List<ConversationItemBase> MixedItems(int count)
    {
        List<ConversationItemBase> items = new(count);
        for (int i = 0; i < count; i++)
        {
            ConversationItemBase item = (i % 4) switch
            {
                0 => ConversationItemFactory.CreateUser($"请看第 {i} 步"),
                1 => new ThinkingItem { Message = $"想一想 {i}", IsDone = true },
                2 => new ToolCallItem
                {
                    CallId = $"call-{i}",
                    ToolName = "Read",
                    ArgumentSummary = $"src/file{i}.cs",
                    ResultText = string.Join('\n', Enumerable.Range(0, 20).Select(x => $"line {x}")),
                    IsRunning = false,
                },
                _ => new TextConversationItem(false) { SenderName = "助手", Message = $"第 {i} 步完成。" },
            };

            item.SourceMessage = new ChatMessage(i % 4 == 0 ? ChatRole.User : ChatRole.Assistant, $"m{i}");
            items.Add(item);
        }

        return items;
    }

    /// <param name="vm">视图模型</param>
    /// <returns>已显示并完成首次排版的窗口</returns>
    private static (Window Window, ConversationView View) ShowView(ConversationViewModel vm)
    {
        ConversationView view = new() { DataContext = vm };
        Window window = new() { Width = WindowWidth, Height = WindowHeight, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    /// <summary>
    /// 造一批够长的 markdown 回复。<b>带代码块</b>：真实回复里代码块才是最重的那块视觉树
    /// （边框 + 语言标签 + 两个按钮 + 滚动容器），只堆段落量不出卸载的收益——
    /// markdown 的段落是 Inline，根本不是可视对象。
    /// </summary>
    /// <param name="count">条目数</param>
    /// <returns>条目列表</returns>
    private static List<ConversationItemBase> MarkdownItems(int count)
    {
        List<ConversationItemBase> items = new(count);
        for (int i = 0; i < count; i++)
        {
            string body = $"## 第 {i} 步\n\n" +
                          string.Join("\n\n", Enumerable.Range(0, 4)
                              .Select(x => $"这是第 {i} 条消息的第 {x} 段正文，写长一点好让气泡有真实高度。")) +
                          $"\n\n```csharp\nvar x{i} = Compute({i});\nConsole.WriteLine(x{i});\n```\n";

            TextConversationItem item = new(false) { SenderName = "助手", Message = body };
            item.SourceMessage = new ChatMessage(ChatRole.Assistant, body);
            items.Add(item);
        }

        return items;
    }

    /// <summary>会话流那个滚动容器</summary>
    private static ScrollViewer Viewer(Visual root) =>
        root.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.Name == "Viewer");

    /// <summary>所有气泡正文控件</summary>
    private static List<SimpleMarkdownViewer> Bubbles(Visual root) =>
        MessageList(root).GetVisualDescendants().OfType<SimpleMarkdownViewer>().ToList();

    /// <summary>把排队与后台任务跑完，再排一次版</summary>
    private static void Settle(Window window)
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.UpdateLayout();
        }
    }

    /// <summary>取会话流那个列表控件（视图里还有别的 ItemsControl，按名字认）</summary>
    private static ItemsControl MessageList(Visual root) =>
        root.GetVisualDescendants().OfType<ItemsControl>().First(x => x.Name == "MessageList");

    /// <summary>已经实体化出容器的条目数</summary>
    private static int RealisedBubbles(Visual root) => MessageList(root).GetRealizedContainers().Count();

    /// <summary>
    /// 会话流<b>没有虚拟化</b>：有几个条目就实体化几个容器。
    ///
    /// 这条事实是整套裁剪口径的前提（<c>ConversationItemWindowTrimmer</c> 的两个上限、
    /// <c>HistoryWindow</c> 的首屏分批，全都因为它才存在）。哪天列表换成虚拟化的，
    /// 这个测试会红——那时该做的是回头把那些常数与注释一起改掉，而不是把它删了。
    /// </summary>
    [Fact]
    public void MessageList_RealisesEveryItem_BecauseItIsNotVirtualised() => HeadlessUi.Run(() =>
    {
        ConversationViewModel vm = new();
        foreach (ConversationItemBase item in MixedItems(40)) vm.Items.Add(item);

        (Window window, ConversationView view) = ShowView(vm);

        Assert.Equal(40, RealisedBubbles(view));
        window.Close();
    });

    /// <summary>
    /// 裁剪的收益要落在<b>视觉树</b>上，不只是集合上。
    ///
    /// 顺带把两档上限的排版耗时打出来：这两个数就是「切回会话卡多久」的量级，
    /// 也是 <c>DefaultBackgroundMaxItems</c> 取一屏量级的依据。
    /// </summary>
    [Fact]
    public void Trimming_ShrinksTheVisualTreeAndTheLayoutCost() => HeadlessUi.Run(() =>
    {
        ConversationViewModel vm = new();
        foreach (ConversationItemBase item in MixedItems(200)) vm.Items.Add(item);

        Stopwatch watch = Stopwatch.StartNew();
        (Window window, ConversationView view) = ShowView(vm);
        long longLayoutMs = watch.ElapsedMilliseconds;

        Assert.Equal(200, RealisedBubbles(view));
        double longExtent = view.GetVisualDescendants().OfType<ScrollViewer>().First().Extent.Height;

        // 裁到后台上限:集合一缩,容器与滚动区高度都得跟着缩
        while (vm.Items.Count > ConversationItemWindowTrimmer.DefaultBackgroundMaxItems) vm.Items.RemoveAt(0);
        watch.Restart();
        window.UpdateLayout();
        long trimmedLayoutMs = watch.ElapsedMilliseconds;

        int trimmed = RealisedBubbles(view);
        double trimmedExtent = view.GetVisualDescendants().OfType<ScrollViewer>().First().Extent.Height;

        output.WriteLine($"200 条:实体化 200 个容器,首次排版 {longLayoutMs}ms,滚动区 {longExtent:F0}px");
        output.WriteLine($"裁到 {trimmed} 条:重排 {trimmedLayoutMs}ms,滚动区 {trimmedExtent:F0}px");

        Assert.Equal(ConversationItemWindowTrimmer.DefaultBackgroundMaxItems, trimmed);
        Assert.True(trimmedExtent < longExtent);
        window.Close();
    });

    /// <summary>
    /// 滚远的气泡卸载成<b>等高</b>占位：正文那棵树垮下去，而滚动区高度一分不动。
    ///
    /// 「等高」是这条路子与真·虚拟化的全部区别，也是唯一的验收口径——Extent 一旦变了，
    /// 跟底、滚到顶续窗、前插补偿三条路径就全部跟着错位，那正是当初放弃虚拟化面板的原因
    /// （见 ADR 0041）。所以这里<b>先断言 Extent 不变</b>，再谈省了多少。
    /// </summary>
    [Fact]
    public void ScrollingFarAway_UnloadsBubbles_WithoutMovingTheExtent() => HeadlessUi.Run(() =>
    {
        ConversationViewModel vm = new() { IsPlaintext = false };
        foreach (ConversationItemBase item in MarkdownItems(60)) vm.Items.Add(item);

        (Window window, ConversationView view) = ShowView(vm);
        ScrollViewer viewer = Viewer(view);

        // 全部转出来 = 用户「从头翻到尾」之后的状态,内存最高的那一刻。
        // 只排版、不跑后台队列:清扫是排在 Background 上的,跑了就量不到卸载前的样子
        foreach (SimpleMarkdownViewer bubble in Bubbles(view)) bubble.RealizeNow();
        window.UpdateLayout();

        double extentBefore = viewer.Extent.Height;
        int listBefore = MessageList(view).GetVisualDescendants().Count();
        int bodyBefore = Bubbles(view).Sum(x => x.GetVisualDescendants().Count());

        // 放清扫过去跑:此刻视口在顶部,底下那些都落在缓冲区之外
        Settle(window);

        double extentAfter = viewer.Extent.Height;
        int listAfter = MessageList(view).GetVisualDescendants().Count();
        int bodyAfter = Bubbles(view).Sum(x => x.GetVisualDescendants().Count());

        output.WriteLine($"全部实化:正文 {bodyBefore} / 整表 {listBefore} 个可视对象,滚动区 {extentBefore:F0}px");
        output.WriteLine($"清扫之后:正文 {bodyAfter} / 整表 {listAfter} 个可视对象,滚动区 {extentAfter:F0}px");

        // 滚动区分毫不动:这条错了,上面三条滚动路径就全错
        Assert.Equal(extentBefore, extentAfter, 1);

        // 正文那一半垮下去了
        Assert.True(bodyAfter * 3 < bodyBefore, $"正文视觉树没有显著变小:{bodyBefore} -> {bodyAfter}");

        // 剩下的是卡片外壳(头像/时间戳/气泡边框/操作行),本机制够不着——要继续省就得把同一套手法
        // 往外套一层。这条断言把「还剩多少」钉住,省得日后误以为卸载已经把列表清空了
        Assert.True(listAfter > listBefore / 2, $"整表少得太多,外壳是不是被一起拆了:{listBefore} -> {listAfter}");
        window.Close();
    });
}
