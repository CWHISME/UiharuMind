using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 化身那一轮（ADR 0055）：投递它没听过的，说完的话以用户的名义进群并开下一波；结局按结束离席、进群与否判定。
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

    [Fact]
    public async Task Push_PostsAsTheUser_AndOpensTheNextWave()
    {
        await _coordinator.PostAsync(_group, "@Alice 先看看");
        _runner.Replies[_avatar.SessionId] = _ => "@Bob 就用 Alice 的方案";
        Task<GroupEpisodeSummary> nextWave = NextEpisode();

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);
        await nextWave;

        Assert.Equal(EGroupAvatarTurnResult.Pushed, turn.Result);
        ChatMessage post = _group.History.Single(x => ChatMessageAnnotations.GroupAvatarPostOf(x) != null);
        Assert.Equal(ChatRole.User, post.Role);
        Assert.Equal(UserName, post.AuthorName);
        // 它听到的是 Alice 的话，不含它自己的；Bob 听到的就是用户说的
        Assert.Contains("[Alice]: Alice 的第 1 次发言", _runner.Calls.Single(x => x.Member == _avatar).Input);
        Assert.Contains($"[{UserName}]: @Bob 就用 Alice 的方案", _runner.Calls.Single(x => x.Member == _bob).Input);
        Assert.Equal(_group.History.Count, _avatar.GroupCursor + 2); //它那句与 Bob 的回复都在游标之后
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
        Assert.Empty(_group.History); //结束之后的话不进群
    }

    /// <summary>化身没有「不接话」这回事（ADR 0060）：写了什么就以用户的名义进群，「[沉默]」也不例外</summary>
    [Fact]
    public async Task PassMarker_IsNotSpecialForTheAvatar()
    {
        _runner.Replies[_avatar.SessionId] = _ => GroupTranscript.PassReply;

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Pushed, turn.Result);
        Assert.Equal(GroupTranscript.PassReply, Assert.Single(_group.History, x => x.Role == ChatRole.User).Text);
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
