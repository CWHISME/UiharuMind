using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Search;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 会话列表的「消息里提到的」：点了才扫、扫这一侧全部会话（不只是标题匹配的）、结果按列表顺序、
/// 只采纳最新一次扫描，点摘要带着扫出它的那个词。走真的扫描，只把读文件换成内存里的行
/// </summary>
public class SessionContentSearchViewDataTests
{
    private readonly List<SessionListItem> _sessions =
    [
        Item("a", "苹果"),
        Item("b", "香蕉"),
    ];

    private readonly Dictionary<string, string[]> _files = new();
    private int _reads;

    private static SessionListItem Item(string id, string title) =>
        new(new ChatSessionMeta { SessionId = id, Title = title, CharacterId = nameof(DefaultCharacter.ChenXiAgent) },
            new RecordingMessageService());

    private void History(string sessionId, params string[] userTexts) =>
        _files[sessionId] = HistoryJsonl.SerializeLines(userTexts.Select(x => new ChatMessage(ChatRole.User, x)))
            .Split('\n');

    private SessionContentSearchViewData Create() => new(() => _sessions, id =>
    {
        Interlocked.Increment(ref _reads);
        return _files.TryGetValue(id, out string[]? lines) ? lines : [];
    }, action => action());

    /// <summary>打字只按标题滤：不点「在消息里搜索」就一个文件都不读</summary>
    [Fact]
    public async Task Typing_DoesNotReadAnyFileUntilStarted()
    {
        History("a", "缓存");
        SessionContentSearchViewData search = Create();

        search.SetQuery("缓存");
        await search.Pending;

        Assert.Equal(0, _reads);
        Assert.True(search.CanStart);
        Assert.False(search.IsActive);
    }

    [Fact]
    public async Task Start_ScansEverySession_AndListsTheLatestThreePerSession()
    {
        History("a", "无关");
        History("b", "缓存1", "缓存2", "无关", "缓存3", "缓存4");
        SessionContentSearchViewData search = Create();

        search.SetQuery(" 缓存 ");
        search.Start();
        await search.Pending;

        Assert.Equal(2, _reads);
        SessionContentResultRow row = Assert.Single(search.Results);
        Assert.Equal("香蕉", row.Session.Name);
        Assert.Equal([4, 3, 1], row.Hits.Select(x => x.Reveal.MessageIndex));
        Assert.False(search.IsSearching);
    }

    /// <summary>开扫之后改词：停手一会儿按新词重扫</summary>
    [Fact]
    public async Task AfterStarting_ChangingTheQueryRescans()
    {
        History("a", "苹果派");
        History("b", "香蕉船");
        SessionContentSearchViewData search = Create();
        search.SetQuery("苹果");
        search.Start();
        await search.Pending;

        search.SetQuery("香蕉");
        await search.Pending;

        Assert.Equal("b", Assert.Single(search.Results).SessionId);
    }

    /// <summary>改词之后旧结果还挂着的那一会儿点下去：按扫出它的那个词去会话里找，不是现在框里的词</summary>
    [Fact]
    public async Task Open_CarriesTheQueryThatProducedTheRow()
    {
        History("a", "缓存");
        SessionContentSearchViewData search = Create();
        SessionContentHitRow? opened = null;
        search.OpenRequested += x => opened = x;
        search.SetQuery("缓存");
        search.Start();
        await search.Pending;

        search.SetQuery("缓存命中"); //防抖还没到,旧结果还在
        search.Open(search.Results[0].Hits[0]);

        Assert.Equal(new ConversationSearchReveal("缓存", 0), opened?.Reveal);
    }

    /// <summary>清空搜索框：收起、复位，扫到一半的那次被作废也不会让「正在搜索」挂着</summary>
    [Fact]
    public async Task ClearingTheQuery_CollapsesAndResets_EvenMidScan()
    {
        History("a", "缓存");
        TaskCompletionSource gate = new();
        SessionContentSearchViewData search = new(() => _sessions, id =>
        {
            gate.Task.Wait();
            return _files.TryGetValue(id, out string[]? lines) ? lines : [];
        }, action => action());
        search.SetQuery("缓存");
        search.Start();
        Task scanning = search.Pending;

        search.SetQuery("  ");
        gate.SetResult();
        await scanning;

        Assert.False(search.IsActive);
        Assert.False(search.IsSearching);
        Assert.Empty(search.Results);
        Assert.Equal(string.Empty, search.HeaderText);

        search.SetQuery("又打了");
        Assert.True(search.CanStart); //下次要再点
    }

    [Fact]
    public async Task RemovedSession_DropsItsRow()
    {
        History("a", "缓存");
        History("b", "缓存");
        SessionContentSearchViewData search = Create();
        search.SetQuery("缓存");
        search.Start();
        await search.Pending;

        search.NotifySessionRemoved("a");

        Assert.Equal("b", Assert.Single(search.Results).SessionId);
    }
}
