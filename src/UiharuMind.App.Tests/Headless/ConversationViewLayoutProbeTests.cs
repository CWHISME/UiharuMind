using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.App.Tests.TestDoubles;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 主题四探针：条目数 → 布局耗时 / 可视对象 / 托管堆的实测曲线。
///
/// 会话流没有虚拟化（<c>ItemsControl</c> 容器一个不少），「留多少条目 = 排多久版」；
/// <c>ConversationItemWindowTrimmer</c> 注释明确写着：探针给出曲线之前，80/10 两档上限
/// 只是凭感觉的魔法数。这里把曲线打出来：每档条目数开一次真实窗口排一次版，
/// 量首次排版耗时、实体化容器数、可视对象数、排版前后托管堆增量。
/// 数据经 ITestOutputHelper 打印（用 --filter-class 跑本类即可看），断言只钉
/// 「容器数=条目数」这条非虚拟化事实，防止探针被误删或误改成虚拟化后不报。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationViewLayoutProbeTests(ITestOutputHelper output)
{
    private const double WindowWidth = 900;
    private const double WindowHeight = 700;

    /// <summary>造一屏混合条目：思考段 + 正文 + 工具卡，贴近真实一次回复的结构</summary>
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

    private static ItemsControl MessageList(Visual root) =>
        root.GetVisualDescendants().OfType<ItemsControl>().First(x => x.Name == "MessageList");

    private static int RealisedContainers(Visual root) => MessageList(root).GetRealizedContainers().Count();

    /// <summary>
    /// 每档条目数开一次真实窗口排一次版，量首次排版耗时与视觉树/托管堆。
    /// 数据是主题四「分档上限要不要分、怎么分」的依据。
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(160)]
    [InlineData(320)]
    [InlineData(640)]
    [InlineData(1280)]
    public void LayoutCost_ScalesWithItemCount_Probe(int count) => HeadlessUi.Run(() =>
    {
        ConversationViewModel vm = new(new RecordingMessageService());
        foreach (ConversationItemBase item in MixedItems(count)) vm.Items.Add(item);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memBefore = GC.GetTotalMemory(true);

        ConversationView view = new() { DataContext = vm };
        Window window = new() { Width = WindowWidth, Height = WindowHeight, Content = view };
        Stopwatch watch = Stopwatch.StartNew();
        window.Show();
        window.UpdateLayout();
        long layoutMs = watch.ElapsedMilliseconds;

        long memAfter = GC.GetTotalMemory(true);
        int containers = RealisedContainers(view);
        int visuals = MessageList(view).GetVisualDescendants().Count();

        output.WriteLine($"条目 {count,5}: 首次排版 {layoutMs,4}ms, 容器 {containers,5}, 可视对象 {visuals,6}, 排版前后托管堆 {(memAfter - memBefore) / 1024,6} KB");

        // 非虚拟化事实：条目数 = 容器数。哪天列表真的虚拟化了，这条会红——正是改常数的信号
        Assert.Equal(count, containers);
        window.Close();
    });
}
