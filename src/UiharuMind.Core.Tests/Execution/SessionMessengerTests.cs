using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// SendMessage 的单聊分支：to 按标题/短号认普通单聊（排除子会话、群壳、群成员），
/// 不忙就以用户的名义落一条消息并让它回一轮，忙则如实报错。
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
    public void Post_AppendsUserMessage_AndRunsATurn()
    {
        ChatSession chat = NewSession("初春的日常");
        List<(string SessionId, string Text)> ran = [];
        SessionMessenger.TurnRunner = (s, m, _, _, _) =>
        {
            ran.Add((s.SessionId, m.Text));
            return Task.FromResult(true);
        };

        string receipt = SessionMessenger.Post(metaOf(chat), "帮我看看这份方案");

        Assert.Contains("已以用户的名义", receipt);
        Assert.Equal("帮我看看这份方案", chat.History[^1].Text);
        Assert.Equal(ChatRole.User, chat.History[^1].Role);
        Assert.Equal((chat.SessionId, "帮我看看这份方案"), Assert.Single(ran));
    }

    [Fact]
    public void Post_WhileRunning_RefusesAndLeavesHistoryUntouched()
    {
        ChatSession chat = NewSession("初春的日常");
        List<(string, string)> ran = [];
        SessionMessenger.TurnRunner = (s, m, _, _, _) => { ran.Add((s.SessionId, m.Text)); return Task.FromResult(true); };

        using (SessionManager.Instance.Running.BeginRun(chat.SessionId))
        {
            string receipt = SessionMessenger.Post(metaOf(chat), "忙的时候别发");

            Assert.StartsWith("Error:", receipt);
            Assert.Contains("正在跑", receipt);
            Assert.Empty(chat.History);
            Assert.Empty(ran); //没跑轮
        }
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