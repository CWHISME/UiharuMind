using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 停止/失败收尾的<b>内容级</b>补存：思考与正文一起按原样落盘（ADR 0110），
/// 无头轮次同样不丢半截。这些行为由 <c>InterruptedStreamSaver</c> 重建 Contents、
/// <c>TurnDriver</c> 在消费循环里攒原始缓冲——老的「从渲染落点取正文」实现这几条全丢。
/// </summary>
public class TurnDriverInterruptedSaveTests
{
    private static ChatSession NewSession()
    {
        return new ChatSession("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
    }

    private static ChatMessage Prompt(string text = "开工") => new(ChatRole.User, text);

    /// <summary>一段内容流：吐完给定内容后当场被掐断（等价于取消标记在枚举中途生效）</summary>
    private static Func<IEnumerable<AIContent>> Interrupted(params AIContent[] before)
    {
        return () => InterruptedCore(before);
    }

    private static IEnumerable<AIContent> InterruptedCore(AIContent[] before)
    {
        foreach (AIContent content in before) yield return content;
        throw new OperationCanceledException();
    }

    private static Func<IEnumerable<AIContent>> Round(params AIContent[] contents) => () => contents;

    /// <summary>
    /// 停在<b>思考段</b>（正文还没开始）：原来的实现 <c>TakeStreamingText</c> 返回 null、
    /// 整条半截回复连思考带正文全都丢。现在缓冲里攒着思考，收尾按原样落盘。
    /// </summary>
    [Fact]
    public async Task Cancelled_WithThinkingOnly_SavesTheThinking()
    {
        ChatSession session = NewSession();
        session.History.Add(Prompt());

        StubRunner runner = new(Interrupted(new TextReasoningContent("这一段推理还没说完")));
        TurnDriver driver = new(null, new TurnUsageLedger()); //无头：没有渲染落点

        await driver.RunAsync(session, runner, Prompt(),
            externalCancellation: TestContext.Current.CancellationToken);

        Assert.Equal(2, session.History.Count); //Prompt + 半截思考
        TextReasoningContent reasoning = Assert.IsType<TextReasoningContent>(
            Assert.Single(session.History[1].Contents));
        Assert.Equal("这一段推理还没说完", reasoning.Text);
    }

    /// <summary>
    /// 停在「思考 + 正文都流了一部分」：两者都要按原样进历史，形状与正常完成的消息一致。
    /// </summary>
    [Fact]
    public async Task Cancelled_WithThinkingThenText_SavesBoth()
    {
        ChatSession session = NewSession();
        session.History.Add(Prompt());

        StubRunner runner = new(Interrupted(
            new TextReasoningContent("推理完了"),
            new TextContent("回答写到一半")));
        TurnDriver driver = new(null, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(),
            externalCancellation: TestContext.Current.CancellationToken);

        ChatMessage saved = session.History[1];
        Assert.Equal(2, saved.Contents.Count);
        Assert.Equal("推理完了", Assert.IsType<TextReasoningContent>(saved.Contents[0]).Text);
        Assert.Equal("回答写到一半", Assert.IsType<TextContent>(saved.Contents[1]).Text);
    }

    /// <summary>
    /// 无头轮次（定时任务/群成员/唤醒轮，渲染落点为 null）取消：老实现从落点取正文、
    /// 取到 null 就什么都不写；现在缓冲在驱动侧，不依赖落点。
    /// </summary>
    [Fact]
    public async Task HeadlessCancelled_SavesTheHalfReply()
    {
        ChatSession session = NewSession();
        session.History.Add(Prompt());

        StubRunner runner = new(Interrupted(new TextContent("半截回复")));
        TurnDriver driver = new(null, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(),
            externalCancellation: TestContext.Current.CancellationToken);

        Assert.Equal(2, session.History.Count);
        Assert.Equal("半截回复", session.History[1].Text);
    }

    /// <summary>
    /// 停在「同一服务调用里的第二个思考段」：前面的正文已经随上一次落盘在历史里。
    /// 收尾不能把整条缓冲里的<a>全部</a>内容再写一遍，只补还没落盘的那截（这里是思考）。
    /// </summary>
    [Fact]
    public async Task Cancelled_AfterAPersistedSegment_OnlySavesTheUnpersistedTail()
    {
        ChatSession session = NewSession();
        session.History.Add(Prompt());
        string persisted = new('已', 60);
        session.History.Add(new ChatMessage(ChatRole.Assistant, persisted));

        // 模拟乱序的落盘信号：persisted 已在历史，缓冲里还有同一段思考尚未落盘
        StubRunner runner = new(Interrupted(
            new TextContent(persisted), MessageBoundaryContent.Instance,
            new TextReasoningContent("第二段思考")));
        TurnDriver driver = new(null, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(),
            externalCancellation: TestContext.Current.CancellationToken);

        Assert.Equal(3, session.History.Count); //Prompt + 已落盘正文 + 未落盘思考
        TextReasoningContent reasoning = Assert.IsType<TextReasoningContent>(
            Assert.Single(session.History[2].Contents));
        Assert.Equal("第二段思考", reasoning.Text);
    }

    /// <summary>
    /// 一整发重来（<see cref="StreamRestartContent"/>）：作废的半截内容不能跟新发的一起落盘。
    /// 缓冲在重发信号处清空，收尾只写新发内容。
    /// </summary>
    [Fact]
    public async Task StreamRestart_ClearsThePendingSegment()
    {
        ChatSession session = NewSession();
        session.History.Add(Prompt());

        StubRunner runner = new(Interrupted(
            new TextContent("这半截被重发作废了"),
            StreamRestartContent.Instance,
            new TextContent("重发后的新内容")));
        TurnDriver driver = new(null, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(),
            externalCancellation: TestContext.Current.CancellationToken);

        Assert.Equal(2, session.History.Count);
        Assert.Equal("重发后的新内容", session.History[1].Text);
    }

    /// <summary>
    /// 内容级去重要认得历史里的<b>思考链</b>：只拼正文的话，缓冲里的思考段永远对不上
    /// 已落盘的链，每次取消都会把同样的思考再写一遍。
    /// </summary>
    [Fact]
    public void TrimContents_WithPersistedThinking_TrimsThePersistedChain()
    {
        string thought = new('想', 60);
        string body = new('正', 60);
        List<ChatMessage> history =
        [
            new ChatMessage(ChatRole.Assistant,
                [new TextReasoningContent(thought), new TextContent(body)]),
        ];

        // 缓冲 = 已落盘(思考+正文) + 新的一句话
        List<AIContent> pending =
        [
            new TextReasoningContent(thought),
            new TextContent(body),
            new TextContent("这句话还没落盘"),
        ];

        List<AIContent>? unpersisted =
            InterruptedStreamSaver.TrimContents(history, null, pending);

        Assert.NotNull(unpersisted);
        TextContent tail = Assert.IsType<TextContent>(Assert.Single(unpersisted));
        Assert.Equal("这句话还没落盘", tail.Text);
    }

    /// <summary>
    /// 缓冲里只剩「已落盘的思考+正文」，没有新内容：收尾一个字都不写，
    /// 否则每按一次停止就多一条重复的假回复。
    /// </summary>
    [Fact]
    public void TrimContents_AllPersisted_ReturnsNull()
    {
        string thought = new('想', 60);
        string body = new('正', 60);
        List<ChatMessage> history =
        [
            new ChatMessage(ChatRole.Assistant,
                [new TextReasoningContent(thought), new TextContent(body)]),
        ];

        List<AIContent> pending = [new TextReasoningContent(thought), new TextContent(body)];

        Assert.Null(InterruptedStreamSaver.TrimContents(history, null, pending));
    }

    /// <summary>
    /// 削已落盘前缀正好切在某条内容的<b>中间</b>：该单元按剩余长度重建，头部的已落盘段丢掉。
    /// 构造：历史链 = 「甲」×30（够长），缓冲 = 单个 <see cref="TextContent"/>（已落盘前缀 + 新句），
    /// 切点落在同一单元内部。
    /// </summary>
    [Fact]
    public void TrimContents_CutInsideAUnit_RebuildsItsTail()
    {
        string persisted = new('甲', 30);
        List<ChatMessage> history = [new ChatMessage(ChatRole.Assistant, persisted)];
        const string fresh = "新";

        List<AIContent> pending = [new TextContent(persisted + fresh + fresh + fresh + fresh + fresh)];

        List<AIContent>? unpersisted = InterruptedStreamSaver.TrimContents(history, null, pending);

        Assert.NotNull(unpersisted);
        TextContent tail = Assert.IsType<TextContent>(Assert.Single(unpersisted));
        Assert.Equal(fresh + fresh + fresh + fresh + fresh, tail.Text);
    }

    /// <summary>
    /// 群成员取消补存同样要剥自加的 <c>[名字]:</c> 前缀（与正常路径 SessionChatHistoryProvider 同款）：
    /// 不剥的话重开会话看到带前缀的发言，下一轮供给模型还会学得更起劲
    /// </summary>
    [Fact]
    public async Task Cancelled_GroupMemberReply_StripsTheSpeakerPrefix()
    {
        ChatSession session = NewSession();
        session.GroupId = "group-test"; //IsGroupMember 由 GroupId 非空推导
        session.ChangeCharacter(new CharacterData
        {
            CharacterId = "member",
            CharacterName = "群成员",
        });
        session.History.Add(Prompt());

        StubRunner runner = new(Interrupted(new TextContent("[群成员]: 半截回复")));
        TurnDriver driver = new(null, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(),
            externalCancellation: TestContext.Current.CancellationToken);

        Assert.Equal(2, session.History.Count);
        Assert.Equal("半截回复", session.History[1].Text);
    }

    private sealed class StubRunner : ICharacterRunner
    {
        private readonly Queue<Func<IEnumerable<AIContent>>> _rounds;

        public StubRunner(params Func<IEnumerable<AIContent>>[] rounds) =>
            _rounds = new Queue<Func<IEnumerable<AIContent>>>(rounds);

        public List<List<ChatMessage>> ReceivedRounds { get; } = new();

        public bool HasSession => true;

        public ETurnBusy Busy => ETurnBusy.None;

        public Action? BusyChanged { get; set; }

        public ChatOptions? ChatOptions { get; init; }

        public TurnInputEstimate? InputEstimate { get; init; }

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedRounds.Add(messages.ToList());
            IEnumerable<AIContent> contents = _rounds.Count > 0 ? _rounds.Dequeue()() : [];
            foreach (AIContent content in contents)
            {
                yield return content;
                await Task.Yield();
            }
        }

        public Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveStateAsync() => Task.CompletedTask;

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public Task<EAgentMode> GetModeAsync() => Task.FromResult(EAgentMode.Execute);

        public Task SetModeAsync(EAgentMode mode) => Task.CompletedTask;

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages) => Task.FromResult(false);

        public Task<IReadOnlyCollection<ChatMessage>> CancelInjectionsAsync(
            IReadOnlyCollection<ChatMessage> messages) => Task.FromResult<IReadOnlyCollection<ChatMessage>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}