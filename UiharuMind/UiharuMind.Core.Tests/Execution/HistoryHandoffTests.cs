using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死交接文档式压缩的三件事：什么时候触发、喂给模型的历史从哪开始、标记怎么认。
///
/// 起点判定是承重的：交接文档<b>本身必须包含在供给区间内</b>——它是压缩之后模型能看到的
/// 全部前情，漏掉它等于那段历史白压；而它之前的消息一条都不能进，否则压缩没省下任何东西。
/// </summary>
public class HistoryHandoffTests
{
    private static ChatMessage User(string text) => new(ChatRole.User, text);

    [Fact]
    public void ShouldWrite_FiresAboveThresholdOfInputBudget()
    {
        int budget = HistoryCompaction.InputBudgetFor(128_000); //119808

        Assert.False(HistoryHandoff.ShouldWrite((long)(budget * 0.74), 128_000));
        Assert.True(HistoryHandoff.ShouldWrite((long)(budget * 0.76), 128_000));
    }

    [Fact]
    public void ShouldWrite_FiresBeforeFrameworkTruncation()
    {
        //必须早于框架截断的水位,否则交接还没写成历史就被截掉了
        Assert.True(HistoryHandoff.Threshold < HistoryCompaction.TruncationThreshold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldWrite_NeverFiresWithoutAContextLength(int contextLength)
    {
        Assert.False(HistoryHandoff.ShouldWrite(long.MaxValue, contextLength));
    }

    [Fact]
    public void CreateNote_IsRecognisableAndCarriesTheBody()
    {
        ChatMessage note = HistoryHandoff.CreateNote("做完了 A，正在做 B。");

        Assert.True(HistoryHandoff.IsNote(note));
        //必须是 system:它在供给窗口里是最老的一条,而框架截断从最老的非 system 组开始删,
        //取 assistant 的话占用一冲到截断水位,第一个被删的就是交接文档自己
        Assert.Equal(ChatRole.System, note.Role);
        Assert.Contains("做完了 A", note.Text);
        //不能带 _attribution:那个键的含义是「不落盘」,交接文档丢了就等于那段历史白压
        Assert.False(note.AdditionalProperties!.ContainsKey(ChatMessageAnnotations.Attribution));
    }

    [Fact]
    public void NoteBody_StripsTheModelFacingTitle()
    {
        ChatMessage note = HistoryHandoff.CreateNote("正文");

        Assert.Equal("正文", HistoryHandoff.NoteBody(note.Text));
        Assert.Equal("没有标题的文本", HistoryHandoff.NoteBody("没有标题的文本"));
    }

    [Fact]
    public void SupplyStart_IsZeroWithoutAnyNote()
    {
        List<ChatMessage> history = [User("a"), User("b")];

        Assert.Equal(0, HistoryHandoff.SupplyStartIndex(history));
    }

    [Fact]
    public void SupplyStart_PointsAtTheNoteItself()
    {
        List<ChatMessage> history = [User("a"), User("b"), HistoryHandoff.CreateNote("交接"), User("c")];

        int start = HistoryHandoff.SupplyStartIndex(history);

        Assert.Equal(2, start);
        Assert.True(HistoryHandoff.IsNote(history[start])); //文档本身必须在供给区间内
    }

    [Fact]
    public void SupplyStart_UsesTheLatestNoteWhenCompactedTwice()
    {
        List<ChatMessage> history =
        [
            User("a"), HistoryHandoff.CreateNote("第一次"), User("b"),
            HistoryHandoff.CreateNote("第二次"), User("c"),
        ];

        Assert.Equal(3, HistoryHandoff.SupplyStartIndex(history));
    }

    [Fact]
    public async Task WriteAsync_ReturnsNullOnEmptyOutputInsteadOfWritingABlankNote()
    {
        StubChatClient client = new(" \n ");

        Assert.Null(await HistoryHandoff.WriteAsync(client, [User("a")]));
    }

    [Fact]
    public async Task WriteAsync_SwallowsFailuresSoTheTurnStillEnds()
    {
        ThrowingChatClient client = new();

        Assert.Null(await HistoryHandoff.WriteAsync(client, [User("a")]));
    }

    [Fact]
    public async Task WriteAsync_AppendsTheInstructionAfterTheHistory()
    {
        StubChatClient client = new("交接正文");

        string? note = await HistoryHandoff.WriteAsync(client, [User("聊天内容")]);

        Assert.Equal("交接正文", note);
        Assert.Equal(2, client.Seen.Count); //历史 + 指令
        Assert.Equal("聊天内容", client.Seen[0].Text);
        Assert.Null(client.SeenOptions?.Tools); //不带工具:纯文本产出,给它工具只会跑偏并多烧配额
    }

    private sealed class StubChatClient(string reply) : IChatClient
    {
        public List<ChatMessage> Seen { get; } = [];
        public ChatOptions? SeenOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Seen.AddRange(messages);
            SeenOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new HttpRequestException("429");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
