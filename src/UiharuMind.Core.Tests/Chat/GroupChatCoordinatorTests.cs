using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群聊调度与投递（ADR 0046）。跑模型的那一段换成假的：这里要钉的是「交给谁、交什么、算不算群发言」，
/// 那些对错跟模型无关。会话一律是临时的，不落盘。
/// </summary>
public class GroupChatCoordinatorTests
{
    private readonly Dictionary<string, ChatSession> _sessions = new();
    private readonly FakeGroupMemberTurnRunner _runner = new();
    private readonly GroupChatCoordinator _coordinator;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _bob;

    public GroupChatCoordinatorTests()
    {
        DefaultCharacterManager.Instance.OnInitialize(); //用户发言的署名取自内置用户卡
        _coordinator = new GroupChatCoordinator(_runner, id => _sessions.GetValueOrDefault(id));
        _group = Track(new ChatSession { Title = "会审", IsGroup = true, IsAgentGroup = true, IsTransient = true });
        _alice = Member("Alice");
        _bob = Member("Bob");
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId];
    }

    [Fact]
    public async Task Round_DeliversWhatEachMemberHasNotHeard_InSpeakingOrder()
    {
        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal([_alice.SessionId, _bob.SessionId], _runner.Calls.Select(x => x.Member.SessionId));
        Assert.Contains("大家好", _runner.Calls[0].Input);
        Assert.DoesNotContain("[Alice]", _runner.Calls[0].Input);
        // 后说的人听得到先说的人
        Assert.Contains("[Alice]: Alice 的第 1 次发言", _runner.Calls[1].Input);

        Assert.Equal(3, _group.History.Count);
        Assert.Equal("Alice", _group.History[1].AuthorName);
        Assert.Equal(_alice.CharacterId, ChatMessageAnnotations.GroupSpeakerOf(_group.History[1]));
        Assert.Equal(_bob.SessionId, ChatMessageAnnotations.GroupSpeakerSessionOf(_group.History[2]));
        Assert.False(_coordinator.IsRunning(_group.SessionId)); //一圈即停
    }

    [Fact]
    public async Task NextRound_SkipsOwnPostsAndWhatWasAlreadyDelivered()
    {
        await _coordinator.PostAsync(_group, "大家好");
        _runner.ClearCalls();

        await _coordinator.ContinueAsync(_group);

        // Alice 上一圈之后只多了 Bob 的话：用户那句已经交过，她自己的话本来就在她会话里
        string aliceInput = _runner.Calls[0].Input;
        Assert.Contains("[Bob]: Bob 的第 1 次发言", aliceInput);
        Assert.DoesNotContain("大家好", aliceInput);
        Assert.DoesNotContain("[Alice]", aliceInput);
        Assert.StartsWith(GroupTranscript.FeedHeader + "[Alice]: Alice 的第 2 次发言\n\n" + GroupTranscript.VoiceReminderOpening, _runner.Calls[1].Input);
    }

    /// <summary>
    /// 群轮被停或失败的成员：投递已交给过他、游标早推过去，没新话也要能被「继续」叫起来接着做；做完就不再叫
    /// </summary>
    [Fact]
    public async Task Serial_InterruptedMember_ResumesOnContinue_EvenWithNothingNew()
    {
        _runner.Fail.Add(_alice.SessionId);
        _runner.Silent = true; //别人不发言：她醒来时没有新话可接
        await _coordinator.PostAsync(_group, "大家好");
        _runner.Fail.Clear();
        _runner.ClearCalls();

        await _coordinator.ContinueAsync(_group);

        (ChatSession member, string input) = Assert.Single(_runner.Calls);
        Assert.Same(_alice, member);
        Assert.StartsWith(GroupTranscript.ResumeNote, input);

        _runner.ClearCalls();
        await _coordinator.ContinueAsync(_group);
        Assert.Empty(_runner.Calls);
    }

    /// <summary>
    /// 附注（如他启动的后台任务跑完了）：群闲着时只叫他一个，附注排在新话之前，他说完照常进群
    /// </summary>
    [Fact]
    public async Task NotifyMember_WhenIdle_WakesOnlyThatMember_NoteBeforeNewLines()
    {
        Assert.True(_coordinator.TryPostFromMember(_bob.SessionId, "我先说一句")); //群闲着:只落盘不叫人

        Assert.True(await _coordinator.NotifyMemberAsync(_alice.SessionId, "后台任务跑完了"));

        (ChatSession member, string input) = Assert.Single(_runner.Calls);
        Assert.Same(_alice, member);
        Assert.StartsWith("后台任务跑完了\n\n" + GroupTranscript.FeedHeader + "[Bob]: 我先说一句", input);
        Assert.Equal(_alice.SessionId, ChatMessageAnnotations.GroupSpeakerSessionOf(_group.History[^1]));

        _runner.ClearCalls();
        await _coordinator.ContinueAsync(_group); //附注交过就不再叫她
        Assert.DoesNotContain(_runner.Calls, x => x.Member == _alice && x.Input.Contains("后台任务跑完了"));
    }

    /// <summary>串行一圈正跑着、他还没轮到：附注随那次投递交出去，圈后不再为它另开一波（离席会把空波记成没进展）</summary>
    [Fact]
    public async Task NotifyMember_DuringSerialRound_RidesTheUpcomingDelivery()
    {
        int episodes = 0;
        _coordinator.EpisodeEnded += _ => Interlocked.Increment(ref episodes);
        Task<bool>? notified = null;
        _runner.During[_alice.SessionId] = () =>
        {
            notified ??= _coordinator.NotifyMemberAsync(_bob.SessionId, "后台任务跑完了");
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        Assert.True(await notified!);

        Assert.Equal(2, _runner.Calls.Count);
        Assert.StartsWith("后台任务跑完了", _runner.Calls[1].Input);
        Assert.Equal(1, episodes);
    }

    /// <summary>他正在私聊（闸被占着）：单叫的那一波一句没说，不算一波；附注留着，下次轮到他照交</summary>
    [Fact]
    public async Task NotifyMember_WhileInPrivateChat_KeepsTheNoteAndRaisesNoEpisode()
    {
        int episodes = 0;
        _coordinator.EpisodeEnded += _ => Interlocked.Increment(ref episodes);

        using (GroupMemberTurnGate.TryEnter(_alice.SessionId)) //临时会话不在索引里,私聊那条 EnterAsync 会直接放行
        {
            Assert.True(await _coordinator.NotifyMemberAsync(_alice.SessionId, "后台任务跑完了"));
        }

        Assert.Empty(_runner.Calls);
        Assert.Equal(0, episodes);

        await _coordinator.ContinueAsync(_group);
        Assert.StartsWith("后台任务跑完了", _runner.Calls.Single(x => x.Member == _alice).Input);
    }

    [Fact]
    public async Task NotifyMember_RefusesNonMembers()
    {
        ChatSession loner = Track(new ChatSession { IsTransient = true });

        Assert.False(await _coordinator.NotifyMemberAsync(loner.SessionId, "后台任务跑完了"));
        Assert.Empty(_runner.Calls);
    }

    /// <summary>
    /// 每轮重锚：投递末尾贴的是<b>本人</b>的口吻（系统提示末尾那句离开口处太远，长群聊里拉不住）
    /// </summary>
    [Fact]
    public async Task EachDelivery_EndsWithTheMembersOwnVoice()
    {
        _alice.CharacterData.PersonaAnchor = "你是Alice。说话短。";

        await _coordinator.PostAsync(_group, "大家好");

        Assert.EndsWith(GroupTranscript.VoiceReminder("你是Alice。说话短。"), _runner.Calls[0].Input);
        Assert.EndsWith(GroupTranscript.VoiceReminder("你是Bob。"), _runner.Calls[1].Input); //没写锚点的回退到名字
    }

    [Theory]
    [InlineData("你是Alice。", "（说话前记着：你是Alice。群里照你自己的说话方式说。）")]
    [InlineData("你是Alice，爱查证", "（说话前记着：你是Alice，爱查证。群里照你自己的说话方式说。）")]
    [InlineData("", "（说话前记着：群里照你自己的说话方式说。）")]
    public void VoiceReminder_JoinsTheAnchorAndTheGroupScale(string coda, string expected)
    {
        Assert.Equal(expected, GroupTranscript.VoiceReminder(coda));
    }

    /// <summary>群里的图：路径引用人人都有，图片本身只转交给看得了图的成员</summary>
    [Fact]
    public async Task PostedImages_ReachOnlyTheMembersWhoCanSeeThem()
    {
        GroupChatCoordinator coordinator = new(_runner, id => _sessions.GetValueOrDefault(id),
            member => member.SessionId == _alice.SessionId);
        DataContent image = new(new byte[] { 1, 2, 3 }, "image/png");

        await coordinator.PostAsync(_group, "看这张\n\n***\n[Attached file: /tmp/a.png]", [image]);

        Assert.Same(image, Assert.Single(_group.History[0].Contents.OfType<DataContent>()));
        ChatMessage toAlice = _alice.History[0];
        ChatMessage toBob = _bob.History[0];
        Assert.Same(image, Assert.Single(toAlice.Contents.OfType<DataContent>()));
        Assert.DoesNotContain(toBob.Contents, x => x is DataContent);
        Assert.Contains("[Attached file: /tmp/a.png]", toBob.Text);
        Assert.True(ChatMessageAnnotations.IsGroupDelivery(toAlice));
    }

    /// <summary>场景在系统提示里（ADR 0048），首次投递不再带：压缩裁掉开头也丢不了</summary>
    [Fact]
    public async Task FirstDelivery_CarriesNoScene()
    {
        await _coordinator.PostAsync(_group, "大家好");

        Assert.DoesNotContain("群聊「会审」", _runner.Calls[0].Input);
        Assert.StartsWith(GroupTranscript.FeedHeader + $"[{_group.History[0].AuthorName}]: 大家好\n\n" + GroupTranscript.VoiceReminderOpening,
            _runner.Calls[0].Input); //用户名取用户卡，不写死
    }

    /// <summary>
    /// SendMessage 发的那条自己就是群发言；它不顶掉这一轮后面的正文——那不是同一句话，
    /// 是又一次开口。顶掉过一次（实测丢掉智能体成员跑完四分钟压测后的判决书），别再顶
    /// </summary>
    [Fact]
    public async Task PostViaSendMessage_DoesNotReplaceTheFinalText()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            Assert.True(_coordinator.TryPostFromMember(_alice.SessionId, "我先去翻一下代码"));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        List<string> alicePosts = _group.History
            .Where(x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId)
            .Select(x => x.Text)
            .ToList();
        Assert.Equal(["我先去翻一下代码", "Alice 的第 1 次发言"], alicePosts);
    }

    /// <summary>
    /// 插话把一轮拆成几段：每次说完（落盘一次服务调用、正文不带工具调用）都立刻进群，
    /// 不等一轮结束、也不只取最后一条——实测并行群里前几段就这么丢了
    /// </summary>
    [Fact]
    public async Task EveryFinishedReplyInATurn_IsPostedAsItLands()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            _alice.History.Add(new ChatMessage(ChatRole.Assistant, "[Alice]: 先回用户一句"));
            _alice.NotifyServiceCallPersisted();
            Assert.Contains(_group.History, x => x.Text == "先回用户一句"); //说完就进群，不等一轮结束

            _alice.History.Add(new ChatMessage(ChatRole.User, "[Bob]: 插进来的一句"));
            _alice.History.Add(new ChatMessage(ChatRole.Assistant,
                [new TextContent("我查一下"), new FunctionCallContent("c1", "Read")]));
            _alice.NotifyServiceCallPersisted();
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        List<string> alicePosts = _group.History
            .Where(x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId)
            .Select(x => x.Text)
            .ToList();
        // 带工具调用的那段是边做边说，不算说完
        Assert.Equal(["先回用户一句", "Alice 的第 1 次发言"], alicePosts);
        // 他自己那边据此分得清：进了群的两条带标记，过程话不带
        Assert.Equal(["[Alice]: 先回用户一句", "Alice 的第 1 次发言"], _alice.History
            .Where(ChatMessageAnnotations.IsPostedToGroup).Select(x => x.Text)); //假跑法不走落盘剥前缀那一关
    }

    /// <summary>
    /// SendMessage 只管它自己所在的那条消息：之后每次说完照常进群。
    /// 曾经按「本轮发过群就不贴正文」收口，于是中途发过一次群的那一轮，末尾那条判决书群里一个字见不到
    /// </summary>
    [Fact]
    public async Task SendMessage_DoesNotSilenceTheRepliesAfterIt()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            Assert.True(_coordinator.TryPostFromMember(_alice.SessionId, "中途先说一句"));
            _alice.History.Add(new ChatMessage(ChatRole.Assistant,
                [new TextContent("我去跑一下"), new FunctionCallContent("c1", "Shell")]));
            _alice.History.Add(new ChatMessage(ChatRole.Assistant, "量完了，结论是这样"));
            _alice.NotifyServiceCallPersisted();
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        List<string> alicePosts = _group.History
            .Where(x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId)
            .Select(x => x.Text)
            .ToList();
        // 带工具调用的那句是边做边说，不进群；之后那次说完照进
        Assert.Equal(["中途先说一句", "量完了，结论是这样", "Alice 的第 1 次发言"], alicePosts);
    }

    /// <summary>被停或失败时，已经说完的几段照常算群发言</summary>
    [Fact]
    public async Task FailedTurn_KeepsTheRepliesAlreadyFinished()
    {
        _runner.Fail.Add(_alice.SessionId);
        _runner.During[_alice.SessionId] = () =>
        {
            _alice.History.Add(new ChatMessage(ChatRole.Assistant, "说完的一段"));
            _alice.NotifyServiceCallPersisted();
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Contains(_group.History, x => x.Text == "说完的一段");
    }

    [Theory]
    [InlineData("[Bob]: 插进来的一句", "Bob", "插进来的一句")]
    [InlineData("[Bob]: 第一行\n[重要]: 第二行", "Bob", "第一行\n[重要]: 第二行")]
    [InlineData("没有前缀", null, "没有前缀")]
    public void ParsePost_IsTheInverseOfFormatPost(string text, string? speaker, string body)
    {
        Assert.Equal(new GroupDeliverySegment(speaker, body), GroupTranscript.ParsePost(text));
        if (speaker != null) Assert.Equal(text, GroupTranscript.FormatPost(speaker, body));
    }

    /// <summary>成员会话里落盘的回复不带自加前缀：他下一轮看到的自己是干净的，学不起来</summary>
    [Fact]
    public void StripOwnPrefix_CleansTheFirstTextInPlace()
    {
        ChatMessage reply = new(ChatRole.Assistant,
            [new TextReasoningContent("想一想"), new TextContent("[白井黑子]: 先别急着客气。")]);

        Assert.True(GroupTranscript.StripOwnPrefix(reply, "白井黑子"));
        Assert.Equal("先别急着客气。", reply.Text);
        Assert.False(GroupTranscript.StripOwnPrefix(reply, "白井黑子")); //再剥一次是空操作

        ChatMessage other = new(ChatRole.Assistant, "[初春饰利]: 不是她自己的名字");
        Assert.False(GroupTranscript.StripOwnPrefix(other, "白井黑子"));
        ChatMessage user = new(ChatRole.User, "[白井黑子]: 用户消息不动");
        Assert.False(GroupTranscript.StripOwnPrefix(user, "白井黑子"));
    }

    [Theory]
    [InlineData("[Alice]: hi", "Alice", "hi")]
    [InlineData("[Alice]：hi", "Alice", "hi")]
    [InlineData("[Alice]:  hi", "Alice", "hi")]
    [InlineData("Alice: hi", "Alice", "hi")]
    [InlineData("Alice：hi", "Alice", "hi")]
    [InlineData("【Alice】: hi", "Alice", "hi")]
    [InlineData("[Alice]: 第一行\n第二行", "Alice", "第一行\n第二行")]
    [InlineData("[Alice]: [Alice]: 双层脏数据", "Alice", "双层脏数据")]
    [InlineData("[Bob]: hi", "Alice", "[Bob]: hi")]
    [InlineData("[重要]: 请查收", "Alice", "[重要]: 请查收")]
    [InlineData("Alice 认为这样可行", "Alice", "Alice 认为这样可行")]
    [InlineData("hi", "Alice", "hi")]
    [InlineData("[alice]: hi", "Alice", "[alice]: hi")] //大小写不一致不算自加前缀，只认原样
    [InlineData("[Alice] 你好", "Alice", "[Alice] 你好")] //中括号无冒号不剥
    [InlineData("[Alice]:", "Alice", "")] //只剩前缀，剥空
    [InlineData("[黑子]: 这可不行呢", "白井黑子", "这可不行呢")] //括号里写的是自称（全名的一段），实测白井黑子这么加
    [InlineData("【初春】：那个……", "初春饰利", "那个……")]
    [InlineData("[子]: 单字不算", "白井黑子", "[子]: 单字不算")]
    [InlineData("黑子：裸名式只认全名", "白井黑子", "黑子：裸名式只认全名")]
    [InlineData("第一段照常说。\n\n[黑子]: 第二段自己又标了一次", "白井黑子", "第一段照常说。\n\n第二段自己又标了一次")]
    [InlineData("第一段\n[御坂美琴]: 引用别人的话", "白井黑子", "第一段\n[御坂美琴]: 引用别人的话")]
    [InlineData("第一段\n黑子：行中裸名不剥", "白井黑子", "第一段\n黑子：行中裸名不剥")]
    public void StripSpeakerPrefix_StripsOnlyOwnPrefix(string body, string speaker, string expected)
    {
        Assert.Equal(expected, GroupTranscript.StripSpeakerPrefix(body, speaker));
    }

    /// <summary>实测：Agnes 的千空两条发言以「[黑猫]:」（用户的名字）开头</summary>
    [Theory]
    [InlineData("[黑猫]: 先把读数贴出来", "石神千空", "黑猫", "先把读数贴出来")]
    [InlineData("【黑猫】：先把读数贴出来", "石神千空", "黑猫", "先把读数贴出来")]
    [InlineData("[黑猫]: [石神千空]: 两层", "石神千空", "黑猫", "两层")]
    [InlineData("[石神千空]: [黑猫]: 两层", "石神千空", "黑猫", "两层")]
    [InlineData("黑猫：这格你来拍", "石神千空", "黑猫", "黑猫：这格你来拍")] //裸名是在对用户说话
    [InlineData("[猫]: 用户名只认全名", "石神千空", "黑猫", "[猫]: 用户名只认全名")]
    [InlineData("第一段\n[黑猫]: 引用用户的话", "石神千空", "黑猫", "第一段\n[黑猫]: 引用用户的话")]
    [InlineData("[黑猫]: 没给用户名就不剥", "石神千空", null, "[黑猫]: 没给用户名就不剥")]
    public void StripMemberPrefix_AlsoStripsLeadingUserPrefix(string body, string speaker, string? userName, string expected)
    {
        Assert.Equal(expected, GroupTranscript.StripMemberPrefix(body, speaker, userName));
    }

    /// <summary>智能体形态中途经群发言工具说话，说完的回复照样进群，不看这一轮调没调过工具（ADR 0060 修订）</summary>
    [Fact]
    public async Task AgentFormMember_PostsMidTurnThroughTheTool_AndTheReplyToo()
    {
        _alice.IsAgentForm = true;
        _runner.During[_alice.SessionId] = () =>
        {
            Assert.True(_coordinator.TryPostFromMember(_alice.SessionId, "我接第一块"));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "开工");

        Assert.Equal(["开工", "我接第一块", "Alice 的第 1 次发言", "Bob 的第 1 次发言"], _group.History.Select(x => x.Text));
    }

    /// <summary>
    /// 智能体形态没调工具，回复就是发言；「[沉默]」才是不接话。实测只经工具说话时，
    /// 用户直接问到他，他照常在正文里答，群里谁也看不见（段 27）
    /// </summary>
    [Fact]
    public async Task AgentFormMember_ReplyIsSpeech_PassIsSilence()
    {
        _alice.IsAgentForm = true;
        _runner.Replies[_alice.SessionId] = n => n == 1 ? "直接在正文里答" : GroupTranscript.PassReply;

        await _coordinator.PostAsync(_group, "开工");
        await _coordinator.PostAsync(_group, "还有吗");

        Assert.Equal(["开工", "直接在正文里答", "Bob 的第 1 次发言", "还有吗", "Bob 的第 2 次发言"],
            _group.History.Select(x => x.Text));
    }

    /// <summary>对话形态的回复就是发言，「[沉默]」是它唯一的不接话写法</summary>
    [Fact]
    public async Task ChatFormMember_PassReply_IsNotPosted()
    {
        _runner.Replies[_alice.SessionId] = _ => GroupTranscript.PassReply;

        await _coordinator.PostAsync(_group, "开工");

        Assert.Equal(["开工", "Bob 的第 1 次发言"], _group.History.Select(x => x.Text));
    }

    [Fact]
    public async Task GroupPostTool_RefusesAPassMarker()
    {
        AIFunction tool = (AIFunction)UiharuMind.Core.AI.Execution.Tools.GroupPostTool.Create(_alice.SessionId);

        object? result = await tool.InvokeAsync(new AIFunctionArguments { ["content"] = "[沉默]" },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("Error: not posted. To stay silent", result?.ToString());
    }

    [Fact]
    public void TryPostFromMember_StripsSelfAddedPrefix()
    {
        Assert.True(_coordinator.TryPostFromMember(_alice.SessionId, "[Alice]: 我先去翻一下代码"));

        ChatMessage post = Assert.Single(_group.History);
        Assert.Equal("我先去翻一下代码", post.Text);
    }

    [Fact]
    public void TryPostFromMember_PrefixOnlyPost_IsRefusedAndNotLogged()
    {
        Assert.False(_coordinator.TryPostFromMember(_alice.SessionId, "[Alice]:"));
        Assert.Empty(_group.History);
    }

    /// <summary>
    /// 同一句话两边是同一时刻：群里那条沿用成员消息的时间戳，
    /// 不因「落盘与追加之间隔了几秒」在分钟精度上错开一位
    /// </summary>
    [Fact]
    public async Task PostedReply_ReusesTheMemberMessageTimestamp()
    {
        DateTimeOffset saidAt = new(2026, 9, 28, 14, 58, 0, TimeSpan.FromHours(8));
        _runner.During[_alice.SessionId] = () =>
        {
            _alice.History.Add(new ChatMessage(ChatRole.Assistant, "掐着点的发言") { CreatedAt = saidAt });
            _alice.NotifyServiceCallPersisted();
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        ChatMessage post = Assert.Single(_group.History, x =>
            ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId && x.Text == "掐着点的发言");
        Assert.Equal(saidAt, post.CreatedAt);
    }

    /// <summary>
    /// 投递盖创建这一刻：不盖的话落盘只能回落成一轮开跑或落盘那一刻，
    /// 中途插话会被标成与实际差几分钟的时间
    /// </summary>
    [Fact]
    public void DeliveryMessage_StampsCreationTime()
    {
        DateTimeOffset before = DateTimeOffset.Now;
        ChatMessage delivery = GroupTranscript.DeliveryMessage("[Alice]: hi", []);
        DateTimeOffset after = DateTimeOffset.Now;

        Assert.NotNull(delivery.CreatedAt);
        Assert.InRange(delivery.CreatedAt.Value, before, after);
    }

    [Fact]
    public async Task SelfPrefixedFinalText_IsStoredClean_AndDeliveredOnce()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            _alice.History.Add(new ChatMessage(ChatRole.Assistant, "[Alice]: 自加前缀的发言"));
            return Task.CompletedTask;
        };
        _runner.Silent = true;

        await _coordinator.PostAsync(_group, "大家好");

        ChatMessage post = Assert.Single(_group.History, x =>
            ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId);
        Assert.Equal("自加前缀的发言", post.Text);
        Assert.Contains("[Alice]: 自加前缀的发言", _runner.Calls[1].Input);
        Assert.DoesNotContain("[Alice]: [Alice]", _runner.Calls[1].Input);
    }

    [Fact]
    public void BuildDelivery_StripsDirtyPrefixInsteadOfDoubling()
    {
        ChatMessage dirty = new(ChatRole.Assistant, "[Alice]: 旧脏数据") { AuthorName = "Alice" };
        ChatMessageAnnotations.MarkGroupPost(dirty, _alice.CharacterId, _alice.SessionId);

        string? delivery = GroupTranscript.BuildDelivery([dirty], 0, _bob.SessionId);

        //剥掉脏前缀再按投递格式重包，仍是单层 [Alice]:；若不剥，这里会是 [Alice]: [Alice]: 旧脏数据
        Assert.Equal(GroupTranscript.FeedHeader + "[Alice]: 旧脏数据", delivery);
    }

    [Fact]
    public async Task UserInterjection_ReachesTheSpeakerOnceAndTheOthersAtTheirTurn()
    {
        _runner.During[_alice.SessionId] = () => _coordinator.PostAsync(_group, "插一句");

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Single(_runner.Injected);
        Assert.Equal(_alice.SessionId, _runner.Injected[0].Member.SessionId);
        Assert.Contains("插一句", _runner.Calls[1].Input); //Bob 轮到时照常收到
        _runner.ClearCalls();

        await _coordinator.ContinueAsync(_group);

        Assert.DoesNotContain("插一句", _runner.Calls[0].Input); //插给过 Alice 的不再投一遍
    }

    /// <summary>
    /// 插话已被读过这件事要随成员会话落盘：只记在调度器内存里的话，重开应用后那几条会随下一轮再投一遍
    /// （实测成员回「你俩重贴的两条，我上轮都回了」）。换一个调度器实例就是重开应用
    /// </summary>
    [Fact]
    public async Task ConsumedInterjection_IsNotRedeliveredAfterRestart()
    {
        _runner.During[_alice.SessionId] = () => _coordinator.PostAsync(_group, "插一句");
        await _coordinator.PostAsync(_group, "大家好");
        _runner.ClearCalls();
        _runner.During.Clear();

        GroupChatCoordinator restarted = new(_runner, id => _sessions.GetValueOrDefault(id));
        await restarted.ContinueAsync(_group);

        Assert.DoesNotContain("插一句", _runner.Calls[0].Input);
    }

    [Fact]
    public async Task UnconsumedInterjection_IsWithdrawnAndDeliveredNextTurn()
    {
        // 插话进了队列，但 Alice 已在最后一次调用里：不撤回的话它会留到她下一轮，紧跟新投递成连续两条 user
        _runner.InjectionsNeverConsumed = true;
        bool interjected = false;
        _runner.During[_alice.SessionId] = () =>
        {
            if (interjected) return Task.CompletedTask;
            interjected = true;
            return _coordinator.PostAsync(_group, "插一句");
        };

        await _coordinator.PostAsync(_group, "大家好");
        _runner.ClearCalls();
        await _coordinator.ContinueAsync(_group);

        Assert.Contains("插一句", _runner.Calls[0].Input); //随她下一轮的投递补上
    }

    [Fact]
    public async Task Stop_EndsTheRoundAndPostsNothingHalfDone()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            _coordinator.Stop(_group.SessionId);
            return Task.CompletedTask;
        };
        _runner.Fail.Add(_alice.SessionId);

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Single(_runner.Calls); //Bob 没轮到
        Assert.Single(_group.History); //只有用户那句
    }

    [Fact]
    public async Task EpisodeEnded_ReportsTheRangeAndFiresAfterTheGroupIsIdle()
    {
        List<GroupEpisodeSummary> ended = [];
        bool idleWhenRaised = false;
        _coordinator.EpisodeEnded += summary =>
        {
            idleWhenRaised = !_coordinator.IsRunning(_group.SessionId);
            ended.Add(summary);
        };

        await _coordinator.PostAsync(_group, "大家好");
        await _coordinator.ContinueAsync(_group);

        Assert.True(idleWhenRaised); //离席要在这里当场开下一波，不能撞上「还在跑」
        Assert.Equal(2, ended.Count);
        Assert.Equal((0, 0, 3, 2, false),
            (ended[0].KickoffPostIndex, ended[0].Start, ended[0].End, ended[0].MemberPostCount, ended[0].Stopped));
        Assert.Equal(((int?)null, 3, 5, 2), (ended[1].KickoffPostIndex, ended[1].Start, ended[1].End, ended[1].MemberPostCount));
    }

    [Fact]
    public async Task EpisodeEnded_KnowsTheEpisodeWasStopped()
    {
        GroupEpisodeSummary? ended = null;
        _coordinator.EpisodeEnded += summary => ended = summary;
        _runner.During[_alice.SessionId] = () =>
        {
            _coordinator.Stop(_group.SessionId);
            return Task.CompletedTask;
        };
        _runner.Fail.Add(_alice.SessionId);

        await _coordinator.PostAsync(_group, "大家好");

        Assert.NotNull(ended);
        Assert.True(ended.Stopped);
        Assert.Equal(0, ended.MemberPostCount);
    }

    [Fact]
    public async Task FailedTurn_PostsNothingButTheRoundGoesOn()
    {
        _runner.Fail.Add(_alice.SessionId);

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal(2, _runner.Calls.Count);
        Assert.DoesNotContain(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId);
    }

    [Fact]
    public void TryPostFromMember_RefusesNonMembers()
    {
        ChatSession loner = Track(new ChatSession { IsTransient = true });

        Assert.False(_coordinator.TryPostFromMember(loner.SessionId, "hi"));
    }

    [Fact]
    public async Task SpeakerChanged_TracksWhoIsSpeakingAcrossTheRound()
    {
        List<string> events = [];
        _coordinator.SpeakerChanged += id => events.Add(id);
        _runner.During[_alice.SessionId] = () =>
        {
            Assert.Equal([_alice.SessionId], _coordinator.SpeakersOf(_group.SessionId));
            return Task.CompletedTask;
        };
        _runner.During[_bob.SessionId] = () =>
        {
            Assert.Equal([_bob.SessionId], _coordinator.SpeakersOf(_group.SessionId));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        Assert.All(events, id => Assert.Equal(_group.SessionId, id));
        Assert.True(events.Count >= 4, $"轮到/结束各应通报一次,实际 {events.Count}");
        Assert.Empty(_coordinator.SpeakersOf(_group.SessionId)); //一圈即停后清空
    }

    [Fact]
    public async Task SpeakerChanged_DoesNotFireWhenNobodyHasNewLines()
    {
        await _coordinator.PostAsync(_group, "大家好"); //第一圈两人都开口
        _runner.Silent = true; //此后成员不再产生群发言,log 不再增长
        await _coordinator.ContinueAsync(_group); //第二圈把各人游标推到 log 尾部
        int fired = 0;
        _coordinator.SpeakerChanged += _ => fired++;

        await _coordinator.ContinueAsync(_group); //第三圈全员没有新话,全部跳过

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Deliveries_AreMarkedSoTheMemberViewCanSplitThem()
    {
        _runner.During[_alice.SessionId] = () => _coordinator.PostAsync(_group, "插一句");

        await _coordinator.PostAsync(_group, "大家好");

        Assert.True(ChatMessageAnnotations.IsGroupDelivery(_alice.History[0])); //投递
        Assert.True(ChatMessageAnnotations.IsGroupDelivery(_bob.History[0]));
        Assert.False(ChatMessageAnnotations.IsGroupDelivery(_alice.History[1])); //他自己的回复不是
    }

    [Fact]
    public void SplitDelivery_SplitsBySpeaker_AndHidesTheReminder()
    {
        string delivery = "（这是群聊「会审」。）\n\n[用户]: 大家好\n\n[Alice]: 第一行\n\n第二段\n[重要]: 不是发言人\n\n"
                           + "[Bob]: 嗯。\n\n" + GroupTranscript.VoiceReminder("你是Carol。");

        IReadOnlyList<GroupDeliverySegment> segments =
            GroupTranscript.SplitDelivery(delivery, ["用户", "Alice", "Bob"]);

        Assert.Equal(
        [
            new GroupDeliverySegment(null, "（这是群聊「会审」。）"),
            new GroupDeliverySegment("用户", "大家好"),
            new GroupDeliverySegment("Alice", "第一行\n\n第二段\n[重要]: 不是发言人"),
            new GroupDeliverySegment("Bob", "嗯。"),
        ], segments);
    }

    /// <summary>补位提示跟在每轮重锚后面，同是说给模型的：一并不画</summary>
    [Fact]
    public void SplitDelivery_HidesTheCatchUpHintWithTheReminder()
    {
        string delivery = "[Bob]: 嗯。\n\n" + GroupTranscript.VoiceReminder("你是Carol。") + "\n\n"
                          + GroupTranscript.CatchUpHint;

        IReadOnlyList<GroupDeliverySegment> segments = GroupTranscript.SplitDelivery(delivery, ["Bob"]);

        Assert.Equal([new GroupDeliverySegment("Bob", "嗯。")], segments);
    }

    /// <summary>打头那行是说给模型的（这是群里的话，不是单独发给他的）：投递与插话都不画它</summary>
    [Fact]
    public void FeedHeader_IsNotRendered()
    {
        string delivery = GroupTranscript.FeedHeader + "[Alice]: 第一行\n\n[Bob]: 嗯。";

        Assert.Equal([new GroupDeliverySegment("Alice", "第一行"), new GroupDeliverySegment("Bob", "嗯。")],
            GroupTranscript.SplitDelivery(delivery, ["Alice", "Bob"]));
        Assert.Equal(new GroupDeliverySegment("Bob", "@Alice 你看"),
            GroupTranscript.ParsePost(GroupTranscript.FormatInjection("Bob", "@Alice 你看")));
    }

    [Fact]
    public void SplitDelivery_UnknownSpeakers_StayOneSegment()
    {
        Assert.Equal([new GroupDeliverySegment(null, "[Carol]: hi")],
            GroupTranscript.SplitDelivery("[Carol]: hi", ["Alice"]));
    }

    [Fact]
    public void Delivery_IsNullWhenNothingIsNew()
    {
        Assert.Null(GroupTranscript.BuildDelivery([], 0, "someone"));
    }

    [Theory]
    [InlineData(false, true)] //普通角色能进任意群
    [InlineData(true, true)] //智能体也收:以 chat 形态加入(ADR 0050),"文件没处落"的问题不存在了;群类型不影响能不能进
    public void CanJoin_IgnoresGroupType(bool isAgent, bool expected)
    {
        Assert.Equal(expected, GroupChatSessions.CanJoin(new CharacterData { IsAgent = isAgent }));
    }

    [Fact]
    public void GroupShell_SidesByGroupTypeNotByItsPlaceholderCharacter()
    {
        ChatSessionMeta agentGroup = new() { IsGroup = true, IsAgentGroup = true };
        ChatSessionMeta chatGroup = new() { IsGroup = true, IsAgentGroup = false };

        Assert.True(SessionManager.IsAgentSide(agentGroup));
        Assert.False(SessionManager.IsChatSide(agentGroup));
        Assert.True(SessionManager.IsChatSide(chatGroup));
        Assert.False(SessionManager.IsAgentSide(chatGroup));
    }

    private ChatSession Member(string name)
    {
        CharacterData character = new() { CharacterId = name.ToLowerInvariant(), CharacterName = name };
        ChatSession member = new(name, character) { IsTransient = true, GroupId = _group.SessionId };
        return Track(member);
    }

    private ChatSession Track(ChatSession session)
    {
        _sessions[session.SessionId] = session;
        return session;
    }
}
