using System.Collections.ObjectModel;
using System.Linq;
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

        /// <summary>此刻裁剪是否不会抽走用户正在读的视口（跟底，或压根不在界面上）</summary>
        public bool CanTrim { get; set; } = true;

        public ConversationItemWindowTrimmer NewTrimmer(int maxItems, int backgroundMaxItems = 20) =>
            new(Items, Window, () => History, () => CanTrim, maxItems, backgroundMaxItems);

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
    public void CannotTrim_DoesNotTrim()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        fixture.CanTrim = false;

        Assert.False(fixture.NewTrimmer(10).TrimIfNeeded());
        Assert.Equal(40, fixture.Items.Count);
    }

    /// <summary>
    /// 整个列表里只有第一条是用户消息（一轮超长的工具链）：往头部退只能退到第一条，
    /// 那等于没东西可裁——宁可超过上限，也不从一轮中间切开
    /// </summary>
    [Fact]
    public void OnlyAnchorIsTheFirstItem_DoesNotTrim()
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
    /// 理想切点落在助手消息上时往头部退：宁可多留一条，也不把一轮切成两半
    /// </summary>
    [Fact]
    public void AnchorSearchStepsBackToThePreviousUserMessage()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);

        Assert.True(fixture.NewTrimmer(11).TrimIfNeeded());

        // 理想切点 29 是助手消息，往头部退到 28；因此实际保留 12 条而不是 11 条
        Assert.Equal(12, fixture.Items.Count);
        Assert.Equal(28, fixture.Window.Start);
    }

    /// <summary>
    /// 一轮很长（agent 会话里几十条工具卡是常态）时也要裁到上限。
    ///
    /// 两种死法都在这一条里：锚点往尾部找会死在「保留的那一段里没有用户消息」；
    /// 只认用户锚点会死在「退到本轮开头 = 整轮留下」——裁是裁了，条目数一点没降，
    /// 这就是「切出去切回来，运行中的会话依旧几百条」的由来。超过硬顶就得从消息起点切。
    /// </summary>
    [Fact]
    public void LongTurnWithNoUserMessageNearTheTail_StillTrims()
    {
        Fixture fixture = new();
        // 两轮:每轮 = 一条用户消息 + 30 条助手侧条目(正文/思考/工具卡)
        for (int turn = 0; turn < 2; turn++)
        {
            ChatMessage user = new(ChatRole.User, $"ask{turn}");
            fixture.History.Add(user);
            fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = user });
            for (int i = 0; i < 30; i++)
            {
                ChatMessage assistant = new(ChatRole.Assistant, $"step{turn}-{i}");
                fixture.History.Add(assistant);
                fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = assistant });
            }
        }

        fixture.Window.Reset(fixture.History.Count);

        // 上限 10:切点落在第二轮尾部,那一段里没有任何用户消息
        Assert.True(fixture.NewTrimmer(80, 10).TrimToBackgroundBudget());

        // 用户锚点(下标 31)会留下 31 条、超过上限 10 的两倍,于是改按消息起点切
        Assert.Equal(10, fixture.Items.Count);
        Assert.Equal(52, fixture.Window.Start);
        Assert.Same(fixture.History[52], fixture.Items[0].SourceMessage);
    }

    /// <summary>
    /// 一轮不长时仍旧只在用户消息处切：从中间切开是长轮次的下策，不该变成默认口径。
    /// </summary>
    [Fact]
    public void ShortTurns_StillCutAtTheUserAnchor()
    {
        Fixture fixture = new();
        // 每轮 = 一条用户消息 + 3 条助手侧条目,上限 10 时用户锚点保下来的远没到硬顶
        for (int turn = 0; turn < 8; turn++)
        {
            ChatMessage user = new(ChatRole.User, $"ask{turn}");
            fixture.History.Add(user);
            fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = user });
            for (int i = 0; i < 3; i++)
            {
                ChatMessage assistant = new(ChatRole.Assistant, $"step{turn}-{i}");
                fixture.History.Add(assistant);
                fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = assistant });
            }
        }

        fixture.Window.Reset(fixture.History.Count);

        Assert.True(fixture.NewTrimmer(80, 10).TrimToBackgroundBudget());

        // 切点 22 往头部退到第六轮的用户消息(下标 20),那一轮因此完整
        Assert.Equal(12, fixture.Items.Count);
        Assert.Equal(ChatRole.User, fixture.Items[0].SourceMessage!.Role);
    }

    /// <summary>
    /// 从一轮中间切时不许把<b>同一条消息</b>摊出来的那几个条目切成两半：
    /// 窗口起点会指向一条已经渲染出一半的消息，「加载更早」再把它整条取回来就是重复渲染。
    /// </summary>
    [Fact]
    public void MidTurnCut_NeverSplitsOneMessagesItems()
    {
        Fixture fixture = new();
        ChatMessage user = new(ChatRole.User, "ask");
        fixture.History.Add(user);
        fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = user });

        // 每条助手消息摊成 4 个条目(思考段 + 正文 + 两张工具卡),共用同一个来源
        for (int i = 0; i < 20; i++)
        {
            ChatMessage assistant = new(ChatRole.Assistant, $"step{i}");
            fixture.History.Add(assistant);
            for (int part = 0; part < 4; part++)
            {
                fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = assistant });
            }
        }

        fixture.Window.Reset(fixture.History.Count);

        Assert.True(fixture.NewTrimmer(80, 10).TrimToBackgroundBudget());

        // 留下的第一条必须是它那条消息的第一个条目:数一数同源条目还剩几个
        ChatMessage first = fixture.Items[0].SourceMessage!;
        Assert.Equal(4, fixture.Items.Count(x => ReferenceEquals(x.SourceMessage, first)));
        Assert.Same(first, fixture.History[fixture.Window.Start]);
    }

    /// <summary>
    /// 待决审批卡永远不会被摘走：卡片没了，回应口还挂在运行循环上，那一轮就永远等不到回应。
    /// 用户锚点那条路顺带保住了它，从消息起点切的那条路必须自己认这条边界。
    /// </summary>
    [Fact]
    public void PendingApprovalCard_IsNeverTrimmedAway()
    {
        Fixture fixture = new();
        ChatMessage user = new(ChatRole.User, "ask");
        fixture.History.Add(user);
        fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = user });

        ApprovalRequestItem approval = new(new ToolApprovalRequestContent(
            "call-1", new FunctionCallContent("call-1", "Write", new Dictionary<string, object?>())));
        int approvalIndex = -1;

        for (int i = 0; i < 40; i++)
        {
            if (i == 5)
            {
                approvalIndex = fixture.Items.Count;
                fixture.Items.Add(approval);
            }

            ChatMessage assistant = new(ChatRole.Assistant, $"step{i}");
            fixture.History.Add(assistant);
            fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = assistant });
        }

        fixture.Window.Reset(fixture.History.Count);
        Assert.True(approvalIndex > 0);

        // 上限 10:理想切点远在审批卡之后,但切点必须收回到卡片之前
        Assert.True(fixture.NewTrimmer(80, 10).TrimToBackgroundBudget());
        Assert.Contains(approval, fixture.Items);
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

    /// <summary>
    /// 切走之后按更紧的上限再裁一遍：当前上限还没到，后台上限已经到了。
    ///
    /// 这一条守的是「切回缓存实例」那条路径——它不走回放，切回的代价就是留下的条目
    /// 一次性重新实体化，所以留多少直接等于卡多久。
    /// </summary>
    [Fact]
    public void BackgroundBudget_TrimsBelowTheRuntimeCap()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        ConversationItemWindowTrimmer trimmer = fixture.NewTrimmer(80, 10);

        Assert.False(trimmer.TrimIfNeeded()); //当前上限 80,没到
        Assert.True(trimmer.TrimToBackgroundBudget());

        Assert.Equal(10, fixture.Items.Count);
        Assert.Equal(30, fixture.Window.Start);
    }

    [Fact]
    public void BackgroundBudget_StillHonoursTheGate()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);
        fixture.CanTrim = false;

        Assert.False(fixture.NewTrimmer(80, 10).TrimToBackgroundBudget());
        Assert.Equal(40, fixture.Items.Count);
    }

    /// <summary>
    /// 正在跑的那一轮不会被摘走：切点落进本轮时，本轮的用户消息在切点<b>之前</b>，
    /// 切点之后剩下的流式条目与工具卡都还没有来源消息，于是找不到锚点、整轮不裁。
    ///
    /// 这是后台裁剪能安全开着的前提——摘走一张待决审批卡，那一轮就永远等不到回应了。
    /// </summary>
    [Fact]
    public void InFlightTurn_IsNeverTrimmedAway()
    {
        Fixture fixture = new Fixture().WithAlternatingTurns(40);

        // 本轮：用户消息已落库,后面的流式正文与工具卡还没回填来源消息
        ChatMessage pending = new(ChatRole.User, "in flight");
        fixture.History.Add(pending);
        fixture.Items.Add(new ProbeItem(fixture.Items) { SourceMessage = pending });
        List<ProbeItem> inFlight = new();
        for (int i = 0; i < 10; i++)
        {
            ProbeItem item = new(fixture.Items);
            inFlight.Add(item);
            fixture.Items.Add(item);
        }

        // 切点落在本轮内部(51 - 5 = 46),往头部退正好退到本轮的用户消息
        Assert.True(fixture.NewTrimmer(80, 5).TrimToBackgroundBudget());

        // 本轮一条不少:用户消息 + 10 条还没回填的流式条目
        Assert.Equal(11, fixture.Items.Count);
        Assert.Same(pending, fixture.Items[0].SourceMessage);
        Assert.All(inFlight, item =>
        {
            Assert.Contains(item, fixture.Items);
            Assert.Equal(0, item.ReleaseCount);
        });
        Assert.Equal(40, fixture.Window.Start);
    }
}
