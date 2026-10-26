using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// SendMessage 的单聊分支：to 按标题/短号认普通单聊（排除子会话、群壳、群成员），
/// 不忙就以用户的名义落一条消息并起等待任务让它回一轮（任务体把最后一句送回来），忙则如实报错。
/// 与 <see cref="ChatSessionToolsTests"/> 同 collection：用全局 <see cref="SessionManager"/>，串行防共享索引污染
/// </summary>
[Collection("ChatSessionState")]
public sealed class SessionMessengerTests : IDisposable
{
    private readonly List<string> _sessions = [];

    public void Dispose()
    {
        foreach (string id in _sessions) SessionManager.Instance.Delete(id);
        SessionMessenger.TurnRunner =
            (session, input, resolver, attended, token) =>
                SessionTurnRunner.RunHeadlessAsync(session, input, resolver, attended, cancellationToken: token); //还原注入
        SessionMessenger.StartWaiter = SessionMessenger.DefaultStartWaiter; //还原注入
    }

    [Fact]
    public void Match_ResolvesSingleChatByTitleAndShortId_AndSkipsSubsGroupsMembers()
    {
        ChatSession chat = NewSession("初春的日常");
        ChatSession sub = new() { Title = "评审草案", ParentSessionId = chat.SessionId, SubAgentType = ESubAgentType.General, IsAgentForm = true };
        SessionManager.Instance.Add(sub);
        _sessions.Add(sub.SessionId);
        ChatSession group = new() { Title = "评审群", IsGroup = true, IsAgentGroup = true };
        SessionManager.Instance.Add(group);
        _sessions.Add(group.SessionId);
        ChatSession member = new() { Title = "评审群 · 初春", GroupId = group.SessionId };
        SessionManager.Instance.Add(member);
        _sessions.Add(member.SessionId);

        Assert.Equal(chat.SessionId, Assert.Single(SessionMessenger.Match("初春的日常")).SessionId);
        Assert.Equal(chat.SessionId, Assert.Single(SessionMessenger.Match("《初春的日常》")).SessionId);
        Assert.Equal(chat.SessionId, Assert.Single(SessionMessenger.Match(SubSessionIdAlias.Short(chat.SessionId))).SessionId);
        Assert.Empty(SessionMessenger.Match("评审草案")); //子会话不接单聊消息
        Assert.Empty(SessionMessenger.Match("评审群")); //群走群通道
        Assert.Empty(SessionMessenger.Match("评审群 · 初春")); //成员走群通道
    }

    [Fact]
    public void Post_AppendsUserMessage_AndStartsWaiterForTheSender()
    {
        ChatSession chat = NewSession("初春的日常");
        List<(string SenderId, string SessionId, string Text)> waited = [];
        SessionMessenger.StartWaiter = (senderId, session, _, input) =>
            waited.Add((senderId, session.SessionId, input.Text));

        string receipt = SessionMessenger.Post(metaOf(chat), "帮我看看这份方案", "sender-session");

        Assert.Contains("已以用户的名义", receipt);
        Assert.Contains("自动送一封回信", receipt);
        Assert.Equal("帮我看看这份方案", chat.History[^1].Text);
        Assert.Equal(ChatRole.User, chat.History[^1].Role);
        // 等待任务的归属是**发送方**：报告按归属送信，填成接收方的话发送方永远等不到
        Assert.Equal(("sender-session", chat.SessionId, "帮我看看这份方案"), Assert.Single(waited));
    }

    [Fact]
    public void Post_WhileRunning_RefusesAndLeavesHistoryUntouched()
    {
        ChatSession chat = NewSession("初春的日常");
        List<(string, string)> waited = [];
        SessionMessenger.StartWaiter = (_, session, _, input) => waited.Add((session.SessionId, input.Text));

        using (SessionManager.Instance.Running.BeginRun(chat.SessionId))
        {
            string receipt = SessionMessenger.Post(metaOf(chat), "忙的时候别发", "sender-session");

            Assert.StartsWith("Error:", receipt);
            Assert.Contains("正在跑", receipt);
            Assert.Empty(chat.History);
            Assert.Empty(waited); //没起任务
        }
    }

    /// <summary>任务体：跑那一轮后，把这一轮追加的段里最后一句助手正文送回去</summary>
    [Fact]
    public async Task RunAndCollectAsync_ReturnsLastAssistantText_BetweenStartAndNow()
    {
        ChatSession chat = NewSession("初春的日常");
        string spoken = "这条是那一轮的回复。";
        SessionMessenger.TurnRunner = (session, input, _, _, _) =>
        {
            session.History.Add(new ChatMessage(ChatRole.Assistant, spoken));
            return Task.FromResult(true);
        };

        int start = chat.History.Count;
        ChatMessage input = chat.CreateMessage(ChatRole.User, "帮我看看这份方案");
        chat.History.Add(input);

        string report = await SessionMessenger.RunAndCollectAsync(chat, start, input, CancellationToken.None);

        Assert.Contains("初春的日常", report);
        Assert.Contains("最后一句是", report);
        Assert.Contains(spoken, report);
    }

    /// <summary>任务体：那一轮没产出可读回复时，如实说明而不是假装有结论</summary>
    [Fact]
    public async Task RunAndCollectAsync_WithNoAssistantReply_SaysNothingReadable()
    {
        ChatSession chat = NewSession("初春的日常");
        SessionMessenger.TurnRunner = (_, _, _, _, _) => Task.FromResult(true); //轮子跑完但没写正文

        int start = chat.History.Count;
        ChatMessage input = chat.CreateMessage(ChatRole.User, "帮我看看这份方案");
        chat.History.Add(input);

        string report = await SessionMessenger.RunAndCollectAsync(chat, start, input, CancellationToken.None);

        Assert.Contains("没有留下可读的回复", report);
    }

    /// <summary>任务体：那一轮失败或被停时如实交代，不能把更早的助手正文当成这一轮的结论</summary>
    [Fact]
    public async Task RunAndCollectAsync_WithFailedTurn_SaysItDidNotFinish()
    {
        ChatSession chat = NewSession("初春的日常");
        chat.History.Add(new ChatMessage(ChatRole.Assistant, "上一轮的话，不能当成这一轮的结论。"));
        SessionMessenger.TurnRunner = (_, _, _, _, _) => Task.FromResult(false); //轮子没跑完

        int start = chat.History.Count;
        ChatMessage input = chat.CreateMessage(ChatRole.User, "帮我看看这份方案");
        chat.History.Add(input);

        string report = await SessionMessenger.RunAndCollectAsync(chat, start, input, CancellationToken.None);

        Assert.Contains("没跑完", report);
        Assert.DoesNotContain("上一轮的话", report);
    }

    /// <summary>叫停等待任务只结束等待：接收方那一轮照常跑完，没权被第三方中止</summary>
    [Fact]
    public async Task RunAndCollectAsync_Cancelled_StopsWaitingButLetsTheTurnRunOn()
    {
        ChatSession chat = NewSession("初春的日常");
        TaskCompletionSource<bool> turnDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool turnCancelled = false;
        SessionMessenger.TurnRunner = (_, _, _, _, token) =>
        {
            token.Register(() => turnCancelled = true);
            return turnDone.Task;
        };

        int start = chat.History.Count;
        ChatMessage input = chat.CreateMessage(ChatRole.User, "帮我看看这份方案");
        chat.History.Add(input);

        using CancellationTokenSource cts = new();
        Task<string> waiting = SessionMessenger.RunAndCollectAsync(chat, start, input, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(turnCancelled);
        turnDone.SetResult(true); //轮次照常跑完
    }

    private ChatSessionMeta metaOf(ChatSession session) => SessionManager.Instance.GetMeta(session.SessionId)!;

    private ChatSession NewSession(string title)
    {
        ChatSession session = new() { Title = title };
        SessionManager.Instance.Add(session);
        _sessions.Add(session.SessionId);
        return session;
    }
}