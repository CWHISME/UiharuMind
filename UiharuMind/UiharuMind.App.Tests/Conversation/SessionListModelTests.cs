using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Shared.Services;
using UiharuMind.Features.Conversation.Pages;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 一页的会话列表。这些行为过去分散在两份代码里（<c>ChatListViewModel</c> 与
/// <c>AgentPageData</c> 内联那一摊），谁都没有测试——而它们恰好是最容易悄悄退化的那些：
/// 列表刷新不能抹掉选中、说过话的会话要浮到顶部、条目要接上换过的元数据。
///
/// 清单来源经 internal 构造换成固定数据，因此不依赖机器上真实的会话；
/// 运行态仍用真的 <c>SessionRunRegistry</c>（纯内存，本身另有 8 个测试）。
/// </summary>
public class SessionListModelTests
{
    private static ChatSessionMeta Meta(string id, string title = "", int minutesAgo = 0)
    {
        return new ChatSessionMeta
        {
            SessionId = id,
            Title = title.Length > 0 ? title : id,
            Description = $"desc-{id}",
            UpdatedAt = DateTimeOffset.Now.AddMinutes(-minutesAgo),
        };
    }

    /// <summary>同步执行的 post：测试里不该有跨线程调度</summary>
    private static SessionListModel Create(Func<List<ChatSessionMeta>> source,
        bool selectNewSessions = false, ESessionListScope scope = ESessionListScope.Agent)
    {
        return new SessionListModel(scope, selectNewSessions, source,
            action => action(), new StubMessageService());
    }

    private static string[] Ids(SessionListModel model) =>
        model.Sessions.Select(x => x.SessionId).ToArray();

    //================= 同步 =================

    [Fact]
    public void Sync_FillsInSourceOrder()
    {
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b"), Meta("c")];

        SessionListModel model = Create(() => metas);

        Assert.Equal(["a", "b", "c"], Ids(model));
    }

    [Fact]
    public void Sync_ReusesItems_SoSelectionSurvives()
    {
        //原先是 Clear + 重填,那会经 ListBox 双向绑定把选中抹成 null,
        //于是 agent 页得靠一个手写的抑制标志绕过去
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b")];
        SessionListModel model = Create(() => metas);
        SessionListItem itemB = model.Sessions[1];
        model.SelectWithoutNotifying(itemB);

        model.Sync();

        Assert.Same(itemB, model.Sessions[1]);
        Assert.Same(itemB, model.SelectedSession);
    }

    [Fact]
    public void Sync_AdoptsTheReplacedMeta()
    {
        //SaveMeta 往索引里放的是一个新的 ChatSessionMeta 对象,抓着旧那份就会一直显示旧标题
        List<ChatSessionMeta> metas = [Meta("a", "旧标题")];
        SessionListModel model = Create(() => metas);
        SessionListItem item = model.Sessions[0];

        metas[0] = Meta("a", "新标题");
        model.Sync();

        Assert.Same(item, model.Sessions[0]);
        Assert.Equal("新标题", item.Name);
    }

    [Fact]
    public void Sync_RefreshesTheTimeString()
    {
        //时间那一行取 meta 的 UpdatedAt。列表此前只在开页时对帐一次,
        //于是说过话之后时间停在打开那一刻——与"浮到顶部"是同一个缺失的通知
        List<ChatSessionMeta> metas = [Meta("a", minutesAgo: 0)];
        SessionListModel model = Create(() => metas);
        SessionListItem item = model.Sessions[0];
        string before = item.TimeString;

        metas[0] = Meta("a");
        metas[0].UpdatedAt = DateTimeOffset.Now.AddDays(-3);
        model.Sync();

        Assert.NotEqual(before, item.TimeString);
        Assert.Equal(DateTimeOffset.Now.AddDays(-3).LocalDateTime.ToString("yyyy/MM/dd"), item.TimeString);
    }

    [Fact]
    public void Sync_MovesItems_WhenOrderChanges()
    {
        //索引按最后更新时间倒序:刚说过话的会话要浮到顶部
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b"), Meta("c")];
        SessionListModel model = Create(() => metas);
        SessionListItem itemC = model.Sessions[2];

        metas.Reverse();
        model.Sync();

        Assert.Equal(["c", "b", "a"], Ids(model));
        Assert.Same(itemC, model.Sessions[0]);
    }

    /// <summary>
    /// 对帐移动条目时，ListBox 的 SelectedItem 双向绑定会被打断并写回 null。
    /// 那不是用户的选择——放它冒出去就会把正在看的会话卸掉、对话区清空，
    /// 而这恰好发生在说过话的会话浮到顶部的那一刻。
    /// 这里用集合变更事件模拟 ListBox 的同步写回
    /// </summary>
    [Fact]
    public void Sync_KeepsSelection_WhenTheBindingWritesBackNullOnMove()
    {
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b"), Meta("c")];
        SessionListModel model = Create(() => metas);
        SessionListItem itemC = model.Sessions[2];
        model.SelectWithoutNotifying(itemC);

        int notified = 0;
        model.SelectionChanged += _ => notified++;
        model.Sessions.CollectionChanged += (_, _) => model.SelectedSession = null;

        metas.Reverse();
        model.Sync();

        Assert.Same(itemC, model.SelectedSession);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void Sync_ClearsSelectionQuietly_WhenTheSelectedSessionIsGone()
    {
        //真消失了才置空,且仍然不通知:接着选谁是各页自己的口径
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b")];
        SessionListModel model = Create(() => metas);
        model.SelectWithoutNotifying(model.Sessions[0]);

        int notified = 0;
        model.SelectionChanged += _ => notified++;

        metas.RemoveAt(0);
        model.Sync();

        Assert.Null(model.SelectedSession);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void Sync_DropsVanishedItems()
    {
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b")];
        SessionListModel model = Create(() => metas);

        metas.RemoveAt(0);
        model.Sync();

        Assert.Equal(["b"], Ids(model));
    }

    [Fact]
    public void Sync_HandlesAddRemoveAndReorderTogether()
    {
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b"), Meta("c")];
        SessionListModel model = Create(() => metas);
        SessionListItem itemC = model.Sessions[2];

        metas.Clear();
        metas.AddRange([Meta("c"), Meta("d"), Meta("a")]);
        model.Sync();

        Assert.Equal(["c", "d", "a"], Ids(model));
        Assert.Same(itemC, model.Sessions[0]); //仍是同一个实例
    }

    //================= 选中 =================

    [Fact]
    public void SelectWithoutNotifying_DoesNotRaiseSelectionChanged()
    {
        //列表变了要让选中跟着对齐,那不是用户的选择,不该触发加载
        SessionListModel model = Create(() => [Meta("a")]);
        int raised = 0;
        model.SelectionChanged += _ => raised++;

        model.SelectWithoutNotifying(model.Sessions[0]);

        Assert.Equal(0, raised);
        Assert.Same(model.Sessions[0], model.SelectedSession);
    }

    [Fact]
    public void Select_RaisesSelectionChanged()
    {
        SessionListModel model = Create(() => [Meta("a")]);
        List<SessionListItem?> raised = new();
        model.SelectionChanged += raised.Add;

        model.SelectedSession = model.Sessions[0];

        Assert.Equal([model.Sessions[0]], raised);
    }

    [Fact]
    public void SelectFirstOrNone_PicksFirst_OrClears()
    {
        List<ChatSessionMeta> metas = [Meta("a"), Meta("b")];
        SessionListModel model = Create(() => metas);

        model.SelectFirstOrNone();
        Assert.Same(model.Sessions[0], model.SelectedSession);

        metas.Clear();
        model.Sync();
        model.SelectFirstOrNone();
        Assert.Null(model.SelectedSession);
    }

    //================= 运行态 =================

    [Fact]
    public void RunStateChange_RefreshesTheMatchingItem()
    {
        string id = Guid.NewGuid().ToString("N"); //不与机器上真实会话撞号
        SessionListModel model = Create(() => [Meta(id)]);
        SessionListItem item = model.Sessions[0];
        Assert.False(item.IsRunning);

        using (SessionManager.Instance.Running.BeginRun(id))
        {
            Assert.True(item.IsRunning);
            Assert.False(item.CanMutateFiles); //跑的过程中不许删除/清空
            Assert.NotNull(item.BusyTip);
        }

        Assert.False(item.IsRunning);
        Assert.True(item.CanMutateFiles);
    }

    [Fact]
    public void RunStateChange_MarksApprovalWaitApart()
    {
        //「等审批」要与「在跑」分开,否则界面无法提示用户回来处理
        string id = Guid.NewGuid().ToString("N");
        SessionListModel model = Create(() => [Meta(id)]);
        SessionListItem item = model.Sessions[0];

        using (SessionManager.Instance.Running.BeginRun(id))
        using (SessionManager.Instance.Running.BeginApprovalWait(id))
        {
            Assert.True(item.IsAwaitingApproval);
            Assert.False(item.IsRunning);
        }
    }

    [Fact]
    public void RunStateChange_ForAnUnknownSession_IsIgnored()
    {
        SessionListModel model = Create(() => [Meta("a")]);

        using (SessionManager.Instance.Running.BeginRun(Guid.NewGuid().ToString("N")))
        {
            Assert.False(model.Sessions[0].IsRunning);
        }
    }

    [Fact]
    public void Dispose_StopsRespondingToRunState()
    {
        string id = Guid.NewGuid().ToString("N");
        SessionListModel model = Create(() => [Meta(id)]);
        SessionListItem item = model.Sessions[0];
        model.Dispose();

        using (SessionManager.Instance.Running.BeginRun(id))
        {
            Assert.False(item.IsRunning); //已解绑,不再被刷
        }
    }

    //================= 条目事件 =================

    /// <summary>
    /// 走条目自己的删除命令。合成的会话标识在磁盘上没有任何文件，
    /// 因此 <c>SessionManager.Delete</c> 走完既不写索引也不抛全局通知，
    /// 只剩条目的 <c>Deleted</c> 那一条路径——正好是这里要测的
    /// </summary>
    private static async Task DeleteAsync(SessionListItem item)
    {
        await item.DeleteCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ItemDeleted_RemovesItAndRaisesRemoved()
    {
        List<ChatSessionMeta> metas = [Meta(Guid.NewGuid().ToString("N")), Meta("b")];
        SessionListModel model = Create(() => metas);
        SessionListItem itemA = model.Sessions[0];
        List<SessionListItem> removed = new();
        model.Removed += removed.Add;

        await DeleteAsync(itemA);

        Assert.Equal(["b"], Ids(model));
        Assert.Equal([itemA], removed);
    }

    [Fact]
    public async Task DeletingTwice_RemovesOnce()
    {
        //摘除后条目已解绑,而摘除本身也对不在列表里的标识免疫——
        //两条路径(条目命令与全局通知)都可能先到,后到的那次必须是空操作
        SessionListModel model = Create(() => [Meta(Guid.NewGuid().ToString("N"))]);
        SessionListItem itemA = model.Sessions[0];
        int removed = 0;
        model.Removed += _ => removed++;

        await DeleteAsync(itemA);
        await DeleteAsync(itemA);

        Assert.Empty(model.Sessions);
        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task Delete_ClearsSelectionWithoutNotifying()
    {
        //删掉当前会话后「接着选谁」是各页自己的口径,module 只负责把选中放掉
        SessionListModel model = Create(() => [Meta(Guid.NewGuid().ToString("N"))]);
        SessionListItem itemA = model.Sessions[0];
        model.SelectWithoutNotifying(itemA);
        int selectionRaised = 0;
        model.SelectionChanged += _ => selectionRaised++;

        await DeleteAsync(itemA);

        Assert.Null(model.SelectedSession);
        Assert.Equal(0, selectionRaised);
    }

    //================= 条目显示 =================

    private static SessionListItem Item(string title, string description)
    {
        return new SessionListItem(
            new ChatSessionMeta { SessionId = "s", Title = title, Description = description },
            new StubMessageService());
    }

    [Fact]
    public void Description_SameAsTitle_IsNotWorthARow()
    {
        //懒建的会话把标题与描述都取自用户第一句,两行显示同一句话只是噪音
        Assert.False(Item("帮我看下这个 bug", "帮我看下这个 bug").HasDistinctDescription);
    }

    [Fact]
    public void Description_Empty_IsNotWorthARow()
    {
        //这是修好之后的常态:懒建的会话不再存那份与标题同源的描述,副行因此让给角色名
        Assert.False(Item("帮我看下这个 bug", string.Empty).HasDistinctDescription);
    }

    [Fact]
    public void Description_FromALegacyTruncatedTitle_IsStillShown()
    {
        //已接受的历史包袱:修之前建的会话标题截断过(30 字 + 省略号),描述是全文,
        //全等判不出来。曾按前缀判重兜住这一类,但 Copy 会给标题加后缀、前缀当场失配,
        //描述照样冒回来——那个启发式在两种情形里各错一次,不如不要。改名一次即可。
        string full = new('长', 50);
        Assert.True(Item(full[..30] + "…", full).HasDistinctDescription);
    }

    [Fact]
    public void Description_SurvivesACopiedTitle()
    {
        //Copy 给标题加 "_Copy" 后缀。真正另一回事的描述(调度器那种)不能因此丢掉
        Assert.True(Item("⏰ 每日巡检_Copy", "检查昨天的构建有没有失败").HasDistinctDescription);
    }

    [Fact]
    public void Description_GenuinelyDifferent_IsShown()
    {
        //调度器建的会话:标题是任务名,描述是任务提示词
        Assert.True(Item("⏰ 每日巡检", "检查昨天的构建有没有失败").HasDistinctDescription);
    }

    [Fact]
    public void Description_IsReevaluated_AfterUpdateMeta()
    {
        SessionListItem item = Item("标题", "另一段描述");
        Assert.True(item.HasDistinctDescription);

        item.UpdateMeta(new ChatSessionMeta { SessionId = "s", Title = "同一句", Description = "同一句" });

        Assert.False(item.HasDistinctDescription);
    }

    private sealed class StubMessageService : IMessageService
    {
        public Task ShowInfoAsync(string message, string? title = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ShowWarningAsync(string message, string? title = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ShowErrorAsync(string message, string? title = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> ConfirmAsync(string message, string? title = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public void ShowNotification(string message, string? title = null,
            MessageSeverity severity = MessageSeverity.Information)
        {
        }
    }
}
