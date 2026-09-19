using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

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
}
