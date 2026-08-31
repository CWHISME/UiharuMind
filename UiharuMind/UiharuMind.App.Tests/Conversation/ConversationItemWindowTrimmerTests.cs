using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 运行期渲染窗口裁剪。
///
/// 这里覆盖的都是「不该裁」的分支：裁错窗口起点比不裁坏得多——那会让「加载更早」
/// 取回错的一段历史，而且症状是静默的（界面上只是消息接不上，没有任何报错）。
/// </summary>
public class ConversationItemWindowTrimmerTests
{
    /// <summary>
    /// 记录 ReleaseImages 调用的条目替身。
    ///
    /// 不用真的 <c>TextConversationItem</c> 加真位图：那要拉起 Avalonia 的图像后端，
    /// 而这里要验的其实只是那条<b>顺序约束</b>——释放必须发生在条目已经从集合里摘掉之后。
    /// 写错不会报错，只会在某一帧渲染时炸，所以只能靠测试守。
    /// </summary>
    private sealed class ProbeItem(ObservableCollection<ConversationItemBase> owner) : ConversationItemBase
    {
        public int ReleaseCount { get; private set; }

        /// <summary>释放那一刻是否还挂在集合上（应当为 false）</summary>
        public bool WasStillAttachedOnRelease { get; private set; }

        public override void ReleaseImages()
        {
            ReleaseCount++;
            if (owner.Contains(this)) WasStillAttachedOnRelease = true;
        }
    }

    /// <summary>
    /// 造一段「用户 → 助手」交替的条目与历史，两边一一对应（真实场景里也基本如此）
    /// </summary>
    private sealed class Fixture
    {
        public ObservableCollection<ConversationItemBase> Items { get; } = new();
        public List<ChatMessage> History { get; } = new();
        public HistoryWindow Window { get; } = new(20);
        public bool IsStuckToBottom { get; set; } = true;

        public ConversationItemWindowTrimmer NewTrimmer(int maxItems) =>
            new(Items, Window, () => History, () => IsStuckToBottom, maxItems);

        /// <param name="count">条目数（偶数下标为用户消息）</param>
        public Fixture WithAlternatingTurns(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ChatMessage message = new(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"m{i}");
                History.Add(message);
                Items.Add(new ProbeItem(Items) { SourceMessage = message });
            }

            Window.Reset(History.Count);
            return this;
        }
    }

    [Fact]
    public void UnderTheCap_DoesNotTrim()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(10);

        Assert.False(fixture.NewTrimmer(20).TrimIfNeeded());
        Assert.Equal(10, fixture.Items.Count);
    }

    /// <summary>
    /// 用户上滚在读旧消息时不裁：他正看的内容会当场消失、视口跳走。
    /// 这一条同时挡掉「滚到顶自动续一窗、下一轮又被裁掉」的乒乓。
    /// </summary>
    [Fact]
    public void NotStuckToBottom_DoesNotTrim()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        fixture.IsStuckToBottom = false;

        Assert.False(fixture.NewTrimmer(10).TrimIfNeeded());
        Assert.Equal(40, fixture.Items.Count);
    }

    /// <summary>
    /// 要保留的那一段里一条用户消息都没有（长工具链）：宁可超过上限也不从中间切开
    /// </summary>
    [Fact]
    public void NoUserAnchorInTheKeptRange_DoesNotTrim()
    {
        Fixture fixture = new();
        ChatMessage user = new(ChatRole.User, "start");
        fixture.History.Add(user);
        fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = user });
        // 其余全是没有来源消息的工具/错误条目,给不出锚点
        for (int i = 0; i < 30; i++) fixture.Items.Add(new ProbeItem(fixture.Items));
        fixture.Window.Reset(fixture.History.Count);

        Assert.False(fixture.NewTrimmer(10).TrimIfNeeded());
        Assert.Equal(31, fixture.Items.Count);
    }

    /// <summary>
    /// 锚点的来源消息在历史里找不到了（用户删过消息）：不裁，而不是裁到一个猜的下标上
    /// </summary>
    [Fact]
    public void AnchorMissingFromHistory_DoesNotTrim()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        int startBefore = fixture.Window.Start;
        fixture.History.Clear(); //历史整体换过一份,条目上那些引用全部失效

        Assert.False(fixture.NewTrimmer(10).TrimIfNeeded());
        Assert.Equal(40, fixture.Items.Count);
        Assert.Equal(startBefore, fixture.Window.Start);
    }

    [Fact]
    public void OverTheCap_TrimsToTheUserAnchorAndMovesTheWindowStart()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);

        Assert.True(fixture.NewTrimmer(10).TrimIfNeeded());

        // 理想切点是 30，那一条正是用户消息（偶数下标），因此锚点就落在 30
        Assert.Equal(10, fixture.Items.Count);
        Assert.Equal(30, fixture.Window.Start);
        Assert.True(fixture.Window.HasEarlier);
        Assert.Same(fixture.History[30], fixture.Items[0].SourceMessage);
    }

    /// <summary>
    /// 理想切点落在助手消息上时往后找：宁可多留一条，也不把一轮切成两半
    /// </summary>
    [Fact]
    public void AnchorSearchSkipsForwardToTheNextUserMessage()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);

        Assert.True(fixture.NewTrimmer(11).TrimIfNeeded());

        // 理想切点 29 是助手消息，往后挪到 30；因此实际保留 10 条而不是 11 条
        Assert.Equal(10, fixture.Items.Count);
        Assert.Equal(30, fixture.Window.Start);
    }

    /// <summary>
    /// 被裁掉的条目各释放一次，且释放时已经不在集合里（顺序反了就是把还在界面上的位图放掉）
    /// </summary>
    [Fact]
    public void TrimmedItems_AreReleasedExactlyOnceAfterDetaching()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        List<ProbeItem> doomed = fixture.Items.Take(30).Cast<ProbeItem>().ToList();
        List<ProbeItem> kept = fixture.Items.Skip(30).Cast<ProbeItem>().ToList();

        Assert.True(fixture.NewTrimmer(10).TrimIfNeeded());

        Assert.All(doomed, item =>
        {
            Assert.Equal(1, item.ReleaseCount);
            Assert.False(item.WasStillAttachedOnRelease);
        });
        Assert.All(kept, item => Assert.Equal(0, item.ReleaseCount));
    }

    /// <summary>
    /// 连着裁两轮：第二轮从上一轮的新起点继续往下挪，而不是回到原点重算
    /// </summary>
    [Fact]
    public void RepeatedTrims_KeepMovingTheWindowForward()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        ConversationItemWindowTrimmer trimmer = fixture.NewTrimmer(10);

        Assert.True(trimmer.TrimIfNeeded());
        Assert.Equal(30, fixture.Window.Start);

        // 再补 10 条新的一轮进来（界面与历史同步增长）
        for (int i = 40; i < 50; i++)
        {
            ChatMessage message = new(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"m{i}");
            fixture.History.Add(message);
            fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = message });
        }

        Assert.True(trimmer.TrimIfNeeded());
        Assert.Equal(10, fixture.Items.Count);
        Assert.Equal(40, fixture.Window.Start);
    }
}
