using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Search;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 跨会话全文搜索：读的是历史文件的行，口径与会话内搜索一致；行级预筛不能漏掉解析后才对得上的命中
/// </summary>
public class SessionContentSearchTests
{
    private readonly Dictionary<string, string[]> _files = new();

    private void File(string sessionId, params ChatMessage[] messages) =>
        _files[sessionId] = HistoryJsonl.SerializeLines(messages).Split('\n');

    private List<SessionContentMatch> Scan(string query, params string[] sessionIds) =>
        SessionContentSearch.Scan(sessionIds, id => _files.TryGetValue(id, out string[]? lines) ? lines : [], query, 3);

    [Fact]
    public void Scan_KeepsTheGivenSessionOrder_AndListsHitsNewestFirst()
    {
        File("a", new ChatMessage(ChatRole.User, "缓存命中率"), new ChatMessage(ChatRole.Assistant, "缓存已修"));
        File("b", new ChatMessage(ChatRole.User, "无关"));
        File("c", new ChatMessage(ChatRole.Assistant, "又是缓存"));

        List<SessionContentMatch> matches = Scan("缓存", "c", "b", "a");

        Assert.Equal(["c", "a"], matches.Select(x => x.SessionId));
        Assert.Equal([1, 0], matches[1].Latest.Select(x => x.MessageIndex));
    }

    [Fact]
    public void Scan_IndexIsTheHistoryIndex_EvenWithBlankLines()
    {
        _files["a"] =
        [
            HistoryJsonl.SerializeLines([new ChatMessage(ChatRole.User, "第一条")]).TrimEnd(),
            "",
            "   ",
            HistoryJsonl.SerializeLines([new ChatMessage(ChatRole.Assistant, "目标在这")]).TrimEnd(),
        ];

        SessionContentHit hit = Assert.Single(Assert.Single(Scan("目标", "a")).Latest);

        Assert.Equal(1, hit.MessageIndex);
    }

    /// <summary>预筛只看原始行：键名、工具结果里的词也会过筛，但解析后按口径判，不该算命中</summary>
    [Fact]
    public void Scan_PrefilterPassesAreStillJudgedByTheDisplayRules()
    {
        File("a", new ChatMessage(ChatRole.User, "contents"),
            new ChatMessage(ChatRole.Assistant, [new FunctionResultContent("c1", "工具里的缓存")]));

        Assert.Empty(Scan("role", "a")); //每行都有 "role" 这个键
        Assert.Empty(Scan("缓存", "a")); //默认不搜工具
    }

    [Theory]
    [InlineData("说\"好\"")] //引号在行里是 \"
    [InlineData("路径 C:\\temp")] //反斜杠在行里是 \\
    [InlineData("好耶🎉")] //emoji 在行里是 \uD83C\uDF89
    public void Scan_FindsKeywordsTheEncoderEscapes(string keyword)
    {
        File("a", new ChatMessage(ChatRole.User, $"前文 {keyword} 后文"));

        Assert.Single(Scan(keyword, "a"));
    }

    [Fact]
    public void Scan_MissingHistory_IsJustNoHit()
    {
        File("a", new ChatMessage(ChatRole.User, "缓存"));

        Assert.Equal(["a"], Scan("缓存", "gone", "a").Select(x => x.SessionId));
    }

    [Fact]
    public void Scan_Cancelled_Throws()
    {
        File("a", new ChatMessage(ChatRole.User, "缓存"));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SessionContentSearch.Scan(["a"], id => _files[id], "缓存",
            3, default, cancelled.Token));
    }

    /// <summary>每个会话只留最近几条（不挂消息本体），总数照数</summary>
    [Fact]
    public void Scan_KeepsOnlyTheLatestHits_ButCountsThemAll()
    {
        File("a", Enumerable.Range(0, 6).Select(i => new ChatMessage(ChatRole.User, $"缓存{i}")).ToArray());

        SessionContentMatch match = Assert.Single(Scan("缓存", "a"));

        Assert.Equal(6, match.HitCount);
        Assert.Equal([5, 4, 3], match.Latest.Select(x => x.MessageIndex));
        Assert.All(match.Latest, x => Assert.True(x.IsUser));
    }

    /// <summary>损坏行照数行序（装载时会跳过它，所以点进去要在会话内重搜，不直接信下标）</summary>
    [Fact]
    public void Scan_CorruptLines_StillCountTowardTheIndex()
    {
        _files["a"] =
        [
            "{ 坏掉的半行",
            HistoryJsonl.SerializeLines([new ChatMessage(ChatRole.User, "目标")]).TrimEnd(),
        ];

        Assert.Equal(1, Assert.Single(Assert.Single(Scan("目标", "a")).Latest).MessageIndex);
    }

    /// <summary>某个会话读不了（读到一半被删、被占）：这一个算没搜到，其余照常</summary>
    [Fact]
    public void Scan_UnreadableSession_IsSkipped()
    {
        File("b", new ChatMessage(ChatRole.User, "缓存"));

        List<SessionContentMatch> matches = SessionContentSearch.Scan(["a", "b"],
            id => id == "a" ? Throwing() : _files[id], "缓存", 3);

        Assert.Equal(["b"], matches.Select(x => x.SessionId));

        static IEnumerable<string> Throwing()
        {
            yield return HistoryJsonl.SerializeLines([new ChatMessage(ChatRole.User, "缓存")]).TrimEnd();
            throw new IOException("file deleted mid-read");
        }
    }

    [Fact]
    public void Scan_PrefilterIgnoresCase()
    {
        File("a", new ChatMessage(ChatRole.Assistant, "用的是 Avalonia 12"));

        Assert.Single(Scan("aVALONIA", "a"));
    }
}
