using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 化身那一轮（ADR 0055 修订：只经 SendMessage 发群）：投递它没听过的，要说的话只能经 SendMessage（<see cref="GroupChatCoordinator.TryPostFromAvatar"/>）
/// 以用户的名义进群并开下一波；回复正文只留它自己的会话。结局按结束离席、进群与否判定。
/// 跑模型的那一段换成假的，会话一律是临时的
/// </summary>
public class GroupAvatarTurnTests
{
    private readonly Dictionary<string, ChatSession> _sessions = new();
    private readonly FakeGroupMemberTurnRunner _runner = new();
    private readonly GroupChatCoordinator _coordinator;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _bob;
    private readonly ChatSession _avatar;

    public GroupAvatarTurnTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        _coordinator = new GroupChatCoordinator(_runner, id => _sessions.GetValueOrDefault(id));
        _group = Track(new ChatSession
        {
            Title = "会审", IsGroup = true, IsAgentGroup = true, IsTransient = true,
            GroupScheduleMode = EGroupScheduleMode.Parallel,
        });
        _alice = Member("Alice");
        _bob = Member("Bob");
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId, Member("Carol").SessionId];
        _avatar = Track(new ChatSession
        {
            CharacterId = nameof(DefaultCharacter.GroupAvatarAgent), GroupId = _group.SessionId,
            IsGroupAvatar = true, IsAgentForm = true, IsTransient = true,
        });
    }

    private static string UserName => CharacterManager.Instance.UserCharacterName;

    /// <summary>化身经 SendMessage 发群：以用户名义进群、开下一波；回复正文只留本地，posted 由工具那一条算</summary>
    [Fact]
    public async Task Push_PostsAsTheUser_AndOpensTheNextWave()
    {
        await _coordinator.PostAsync(_group, "@Alice 先看看");
        _runner.During[_avatar.SessionId] = () =>
        {
            Assert.Equal(EAvatarPostResult.Posted, _coordinator.TryPostFromAvatar(_avatar.SessionId, "@Bob 就用 Alice 的方案"));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => ""; //回复正文不进群
        Task<GroupEpisodeSummary> nextWave = NextEpisode();

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);
        await nextWave;

        Assert.Equal(EGroupAvatarTurnResult.Pushed, turn.Result);
        ChatMessage post = _group.History.Single(x => ChatMessageAnnotations.GroupAvatarPostOf(x) != null);
        Assert.Equal(ChatRole.User, post.Role);
        Assert.Equal(UserName, post.AuthorName);
        // 它听到的是 Alice 的话，不含它自己的；Bob 第一轮先跑完了，化身那句由波末补位投给他
        Assert.Contains("[Alice]: Alice 的第 1 次发言", _runner.Calls.First(x => x.Member == _avatar).Input);
        Assert.Contains($"[{UserName}]: @Bob 就用 Alice 的方案", _runner.Calls.Last(x => x.Member == _bob).Input);
    }

    /// <summary>没调 SendMessage、只写了回复正文：正文不进群（只经 SendMessage），整轮当 Silent——离席控制器按没进展退避再催</summary>
    [Fact]
    public async Task ReplyBody_WithoutSendMessage_IsNotPosted_AndTreatedAsSilent()
    {
        _runner.Replies[_avatar.SessionId] = _ => "@Bob 就用 Alice 的方案"; //照对话习惯在正文里答

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Silent, turn.Result);
        Assert.Empty(_group.History);
    }

    [Fact]
    public async Task EndAway_WinsOverWhatItSaysAfterwards()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?> { ["reason"] = "done", ["summary"] = "做完了" }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "大家辛苦了";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result);
        Assert.Equal(EAwayEndRequest.Done, turn.End?.Reason);
        Assert.Empty(_group.History); //回复正文不进群（只经 SendMessage），结束之后的话更不进群
    }

    /// <summary>告别话走 SendMessage（只经 SendMessage）：先调工具把话发出去、再结束；同条消息里的顺手正文照样只留本地</summary>
    [Fact]
    public async Task Farewell_GoesThroughSendMessage_BeforeEnding()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            Assert.Equal(EAvatarPostResult.Posted, _coordinator.TryPostFromAvatar(_avatar.SessionId, "各位辛苦，两步都做完了"));
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new TextContent("收口可修成立，报个目标达成收尾"),
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?> { ["reason"] = "done", ["summary"] = "做完了" }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => ""; //之后没正文

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result);
        Assert.Equal("做完了", turn.End?.Summary);
        ChatMessage post = Assert.Single(_group.History, x => x.Role == ChatRole.User);
        Assert.Equal("各位辛苦，两步都做完了", post.Text);
        Assert.NotNull(ChatMessageAnnotations.GroupAvatarPostOf(post));
    }

    /// <summary>调了结束离席之后再调 SendMessage：不再接受（结束之后的话不进群，与旧的自动进群同口径）</summary>
    [Fact]
    public async Task EndAway_ThenSendMessage_IsRefused()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?> { ["reason"] = "done", ["summary"] = "做完了" }),
            ]));
            Assert.Equal(EAvatarPostResult.AwayEnded, _coordinator.TryPostFromAvatar(_avatar.SessionId, "迟到的告别话"));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result);
        Assert.Empty(_group.History);
    }

    /// <summary>化身没有「不接话」（ADR 0055 决策 4 随「只经 SendMessage」改）：不调 SendMessage 就是不说话，Silent 由离席退避催——「[沉默]」也不例外</summary>
    [Fact]
    public async Task PassMarker_IsNotSpecialForTheAvatar()
    {
        _runner.Replies[_avatar.SessionId] = _ => GroupTranscript.PassReply;

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Silent, turn.Result);
        Assert.Empty(_group.History);
    }

    [Fact]
    public async Task Silence_IsNoProgress_AndPostsNothing()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, "（你上次没给出下一步。）",
            CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Silent, turn.Result);
        Assert.Empty(_group.History);
        // 没新话也照跑，提示附在末尾
        Assert.Equal($"{GroupAvatarTranscript.NothingNew}\n\n（你上次没给出下一步。）", _runner.Calls.Single().Input);
    }

    [Fact]
    public async Task Failure_AndStop_AreToldApart()
    {
        _runner.Fail.Add(_avatar.SessionId);
        GroupAvatarTurn failed = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);
        _runner.Fail.Clear();

        _runner.During[_avatar.SessionId] = () =>
        {
            Assert.True(_coordinator.IsAvatarRunning(_group.SessionId));
            _coordinator.Stop(_group.SessionId); //群的停止也停化身这一轮
            return Task.CompletedTask;
        };
        GroupAvatarTurn stopped = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Failed, failed.Result);
        Assert.Equal(EGroupAvatarTurnResult.Stopped, stopped.Result);
        Assert.False(_coordinator.IsAvatarRunning(_group.SessionId));
    }

    /// <summary>它已经说出一句、随后被停：停止优先，不按「推进」算——否则不排停止后的延迟</summary>
    [Fact]
    public void Stop_WinsOverWhatWasAlreadySaid()
    {
        Assert.Equal(EGroupAvatarTurnResult.Stopped, GroupAvatarTurn.Classify([], false, true, 1).Result);
        Assert.Equal(EGroupAvatarTurnResult.Pushed, GroupAvatarTurn.Classify([], true, false, 1).Result);
    }

    [Fact]
    public async Task PrivateChatHoldingTheGate_SkipsTheTurn()
    {
        using (GroupMemberTurnGate.TryEnter(_avatar.SessionId))
        {
            GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);
            Assert.Equal(EGroupAvatarTurnResult.Busy, turn.Result);
        }

        Assert.Empty(_runner.Calls);
    }

    private Task<GroupEpisodeSummary> NextEpisode()
    {
        TaskCompletionSource<GroupEpisodeSummary> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _coordinator.EpisodeEnded += summary => ended.TrySetResult(summary);
        return ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private ChatSession Member(string name)
    {
        CharacterData character = new() { CharacterId = name.ToLowerInvariant(), CharacterName = name };
        return Track(new ChatSession(name, character) { IsTransient = true, GroupId = _group.SessionId });
    }

    private ChatSession Track(ChatSession session)
    {
        _sessions[session.SessionId] = session;
        return session;
    }
}