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

    /// <summary>
    /// 结束调用的 say 参数是点名要进群的告别话：同条消息里的顺手正文（自言自语）照样只留本地，一个字不动
    /// </summary>
    [Fact]
    public async Task EndCallSayParameter_IsPosted_WhileSideTextStaysLocal()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new TextContent("收口可修成立，报个目标达成收尾"),
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "做完了", ["say"] = "各位辛苦，两步都做完了",
                    }),
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

    /// <summary>
    /// 结局判定不认 completed（GroupAvatarTurn.Classify 里 FindEnd 非空即 Ended）：失败/被停的轮次只要
    /// 历史里有成立的结束调用，离席照样真结束、回执照样进群——告别话也得跟着进群，不能丢
    /// </summary>
    [Fact]
    public async Task EndCallSay_IsPosted_EvenWhenTheTurnFails()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "做完了", ["say"] = "大家再见",
                    }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Fail.Add(_avatar.SessionId); //这一轮失败,completed=false
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result); //离席真结束
        ChatMessage post = Assert.Single(_group.History, x => x.Role == ChatRole.User);
        Assert.Equal("大家再见", post.Text);
        Assert.NotNull(ChatMessageAnnotations.GroupAvatarPostOf(post));
    }

    /// <summary>
    /// 无限模式：结束调用被拦（endCallsBlocked），FindEnd 认不出，告别话不进群
    /// </summary>
    [Fact]
    public async Task EndCallSay_IsNotPosted_InInfiniteMode()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "做完了", ["say"] = "大家再见",
                    }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None,
            endCallsBlocked: true);

        Assert.Empty(_group.History);
        Assert.NotEqual(EGroupAvatarTurnResult.Ended, turn.Result);
    }

    /// <summary>
    /// 反向的边界也钉住：失败轮次里没有成立的结束调用，告别话自然没有，也不该凭空发一条
    /// </summary>
    [Fact]
    public async Task EndCallSay_IsNotPosted_WhenTheTurnFails_WithoutAValidEndCall()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant, [new TextContent("什么都没干成")]));
            return Task.CompletedTask;
        };
        _runner.Fail.Add(_avatar.SessionId);
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Empty(_group.History);
        Assert.Equal(EGroupAvatarTurnResult.Failed, turn.Result);
    }

    /// <summary>
    /// 被私聊叫停（preempt）的轮次：只要历史里有成立的结束调用，离席照样 Ended（Classify 的 Ended 优先于 Preempted），
    /// 告别话跟着进群——旧版 !completed 守卫会在这条路径上把 say 吞掉
    /// </summary>
    [Fact]
    public async Task EndCallSay_IsPosted_EvenWhenPreempted()
    {
        Task<IDisposable>? privateTurn = null;
        _runner.During[_avatar.SessionId] = () =>
        {
            privateTurn ??= GroupMemberTurnGate.EnterPrivateAsync(_avatar.SessionId); //取消化身那一轮,不 await 免得死锁
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "做完了", ["say"] = "大家再见",
                    }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result); //FindEnd 压过 Preempted
        ChatMessage post = Assert.Single(_group.History, x => x.Role == ChatRole.User);
        Assert.Equal("大家再见", post.Text);
        if (privateTurn != null) (await privateTurn).Dispose();
    }

    /// <summary>
    /// 同一轮多个成立的结束调用：只发第一次的 say（FindEnd 取 FirstOrDefault），与回执 reason/summary 同口径
    /// </summary>
    [Fact]
    public async Task EndCallSay_OnlyTheFirstValidCall_IsPosted()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "做完了", ["say"] = "先说再见",
                    }),
            ]));
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c2", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "再说一遍", ["say"] = "再说再见",
                    }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result);
        ChatMessage post = Assert.Single(_group.History, x => x.Role == ChatRole.User);
        Assert.Equal("先说再见", post.Text);
    }

    /// <summary>
    /// 只有第一个成立调用才算数：它没带 say，第二个带了也不补——钉住「只发第一次」语义，防被误当成漏发 bug
    /// </summary>
    [Fact]
    public async Task EndCallSay_NoSayOnTheFirstValidCall_MeansNothingLeaves()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?> { ["reason"] = "done", ["summary"] = "做完了" }),
            ]));
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c2", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "补一句", ["say"] = "再见",
                    }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result);
        Assert.Empty(_group.History);
    }

    /// <summary>空白 say（Trim 后为空串）不贴空告别</summary>
    [Fact]
    public async Task EndCallSay_WhitespaceOnly_IsNotPosted()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _avatar.History.Add(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("c1", EndAwayTool.ToolName,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "done", ["summary"] = "做完了", ["say"] = "   ",
                    }),
            ]));
            return Task.CompletedTask;
        };
        _runner.Replies[_avatar.SessionId] = _ => "";

        GroupAvatarTurn turn = await _coordinator.RunAvatarAsync(_group, _avatar, null, CancellationToken.None);

        Assert.Equal(EGroupAvatarTurnResult.Ended, turn.Result);
        Assert.Empty(_group.History);
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
