using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群聊的门面与调度宿主（ADR 0046、0049）。「谁、什么时候」交给每波一个的调度策略
/// （<see cref="SerialGroupScheduler"/> / <see cref="ParallelGroupScheduler"/>），这里只管两种模式共用的部分：
///
/// <list type="bullet">
/// <item><b>投递</b>：成员闲着时，轮到他再把游标之后的新发言合成一条交给他</item>
/// <item><b>广播</b>：每条新发言即时插进正在跑的成员那一轮（用户插话是它的特例）</item>
/// <item><b>群流水的唯一写入口</b>：用户发言、成员正文、成员用 SendMessage 发的都经 <see cref="Append"/>，
/// 于是「送达即不可改」（0046 决策 6）与只追加的顺序由构造保证</item>
/// </list>
/// </summary>
public sealed class GroupChatCoordinator : IGroupTurnHost
{
    private readonly IGroupMemberTurnRunner _runner;
    private readonly Func<string, ChatSession?> _load;
    private readonly object _locker = new();
    private readonly Dictionary<string, Episode> _episodes = new(); //群 → 正在跑的那一波
    private readonly Dictionary<string, HashSet<int>> _consumed = new(); //成员会话 → 插话插给他且已被消费的群流水下标

    /// <summary>应用里的那一个：成员用无头编排跑，会话经会话管理器取</summary>
    public static GroupChatCoordinator Instance { get; } =
        new(new HeadlessGroupMemberTurnRunner(), id => SessionManager.Instance.Load(id));

    /// <summary>
    /// 构造一个调度器（测试用来换掉跑模型的那一段）
    /// </summary>
    /// <param name="runner">成员一轮的跑法</param>
    /// <param name="load">按标识取会话</param>
    public GroupChatCoordinator(IGroupMemberTurnRunner runner, Func<string, ChatSession?> load)
    {
        _runner = runner;
        _load = load;
    }

    /// <summary>
    /// 群当前发言人变了（有人开口 / 有人说完）。参数是群壳会话标识。
    /// ⚠️ <b>可能来自后台线程</b>（成员一轮在无头编排上跑），订阅方自行 marshal。
    /// </summary>
    public event Action<string>? SpeakerChanged;

    /// <summary>
    /// 认不认得出「对全群说」。SendMessage 的 to 写这几个就是发群，不是找某个人
    /// </summary>
    /// <param name="to">收件人</param>
    /// <returns>是发群为 true</returns>
    public static bool IsGroupAddress(string? to) =>
        string.Equals(to?.Trim(), "group", StringComparison.OrdinalIgnoreCase) || to?.Trim() is "群" or "全群" or "群里";

    /// <summary>这个群此刻是不是在跑一波</summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>在跑为 true</returns>
    public bool IsRunning(string groupId) => EpisodeOf(groupId) != null;

    /// <summary>
    /// 某群此刻正在发言的成员会话（串行至多一位，并行可能几位同时）。
    /// 界面据此显示「谁在说」（忙碌文案与右栏成员列表的正在说标记）
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>成员会话标识，按发言顺序；没人在说为空</returns>
    public IReadOnlyList<string> SpeakersOf(string groupId) => EpisodeOf(groupId)?.Run.Speakers ?? [];

    /// <summary>
    /// 用户往群里发言。群闲着就开一波；正在跑就广播给正在跑的成员，并按调度模式决定要不要再叫醒谁
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="text">发言</param>
    public async Task PostAsync(ChatSession group, string text)
    {
        ChatMessage post = group.CreateMessage(ChatRole.User, text);
        int index = Append(group, post);

        if (EpisodeOf(group.SessionId) is { } running)
        {
            await OnAppendedAsync(running, post, index, null);
            return;
        }

        // 开波落空说明恰好别处先开了一波：按插进那一波处理，别把这句丢了
        if (!await RunEpisodeAsync(group, new GroupKickoff(index)) && EpisodeOf(group.SessionId) is { } raced)
        {
            await OnAppendedAsync(raced, post, index, null);
        }
    }

    /// <summary>
    /// 不开口也让大家接着说：串行再跑一圈；并行叫醒所有还有新话没听的成员。一个群同时只跑一波，重复调用是空操作
    /// </summary>
    /// <param name="group">群壳会话</param>
    public Task ContinueAsync(ChatSession group) => RunEpisodeAsync(group, new GroupKickoff(null));

    /// <summary>停下这个群正在跑的那一波（连同所有正在说的成员）</summary>
    /// <param name="groupId">群壳会话标识</param>
    public void Stop(string groupId) => EpisodeOf(groupId)?.Run.Cancel();

    /// <summary>
    /// 成员自己用 SendMessage 往群里发一句（0046 决策 4）。调用方不是群成员时返回 false，交回委派那条路
    /// </summary>
    /// <param name="memberSessionId">发言人的会话标识</param>
    /// <param name="content">发言</param>
    /// <returns>发出去了为 true</returns>
    public bool TryPostFromMember(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (_load(memberSessionId) is not { IsGroupMember: true } member) return false;
        if (_load(member.GroupId!) is not { IsGroup: true } group) return false;

        // 广播与唤醒不等：工具调用要立刻拿到回执，那一段在后台照追加顺序做完
        return PostFromMember(group, member, content.Trim()) != null;
    }

    async Task<GroupTurnOutcome> IGroupTurnHost.RunMemberAsync(GroupRun run, string memberSessionId,
        EGroupWakeCause cause)
    {
        ChatSession group = run.Group;
        if (_load(memberSessionId) is not { } member) return GroupTurnOutcome.Skipped;

        // 成员此刻正在私聊（前台轮占着闸）：这次跳过，游标不动，下次补投——不丢话
        using IDisposable? gate = GroupMemberTurnGate.TryEnter(member.SessionId);
        if (gate == null) return GroupTurnOutcome.Skipped;

        int deliveredFrom = member.GroupCursor;
        string? delivery;
        GroupMemberTurnState? turn = null;
        // 取投递、推游标、登记在说，与 Append 同一把锁：之后追加的每一条要么在投递里、要么会插给他，不漏不重
        lock (_locker)
        {
            _consumed.Remove(member.SessionId, out HashSet<int>? consumed);
            delivery = GroupTranscript.BuildDelivery(group.History, member.GroupCursor, member.SessionId, consumed);
            member.GroupCursor = group.History.Count;
            if (delivery != null)
            {
                turn = new GroupMemberTurnState(member, cause, group.History.Count);
                run.Begin(turn);
            }
        }

        // 锁外通报：订阅方会回来问 IsRunning/SpeakersOf
        if (turn != null) SpeakerChanged?.Invoke(group.SessionId);

        // 工作区以群壳为准，每轮开跑前对齐：右栏改的是群的工作区，成员各存一份就会对不上。
        // 成员只在其会话是 agent 形态时才领工作区（ADR 0050：普通群的 agent 卡以 chat 形态加入，不绑）
        member.WorkspacePath = member.IsAgentForm is true ? group.WorkspacePath : null;
        member.SaveMeta(touchUpdatedAt: false);
        if (turn == null) return GroupTurnOutcome.Skipped; //没有新话可接，这次他不开口

        string input = ComposeInput(run, member, delivery!, deliveredFrom);
        // 插话会让一轮说好几次话，每次说完都进群，而不是只取最后一条
        using GroupMemberReplyFeed replies = new(group, member, turn.Cursor, text => PostFromMember(group, member, text));
        bool completed = await RunTurnAsync(member, input, run.Token);
        replies.Finish(completed);

        IReadOnlySet<int> consumedNow = await turn.CloseAsync(_runner);
        lock (_locker)
        {
            if (consumedNow.Count > 0) _consumed[member.SessionId] = [..consumedNow];
        }

        if (run.End(member.SessionId)) SpeakerChanged?.Invoke(group.SessionId);
        await replies.WhenPostedAsync();

        // 失败或被停：已经说完的几段照常算，没说完的半截留在他自己的会话里
        return new GroupTurnOutcome(completed, consumedNow);
    }

    bool IGroupTurnHost.HasNewLines(ChatSession group, string memberSessionId)
    {
        if (_load(memberSessionId) is not { } member) return false;

        lock (_locker)
        {
            _consumed.TryGetValue(memberSessionId, out HashSet<int>? consumed);
            return GroupTranscript.BuildDelivery(group.History, member.GroupCursor, memberSessionId, consumed) != null;
        }
    }

    /// <inheritdoc cref="IGroupTurnHost.RosterOf" />
    /// 名单形状与 <see cref="SessionManager.MemberMetasOf"/> 同源；这里走注入的 <c>_load</c>
    /// （测试拿 fake 会话喂），不能换成全局单例，所以不直接调共享助手
    public IReadOnlyList<GroupRosterEntry> RosterOf(ChatSession group)
    {
        List<GroupRosterEntry> roster = [];
        foreach (string id in group.GroupMemberSessionIds)
        {
            if (_load(id) is { } member) roster.Add(new GroupRosterEntry(id, member.CharacterData.CharacterName));
        }

        return roster;
    }

    private async Task<bool> RunEpisodeAsync(ChatSession group, GroupKickoff kickoff)
    {
        Episode episode;
        lock (_locker)
        {
            if (_episodes.ContainsKey(group.SessionId)) return false;

            GroupRun run = new(group);
            IGroupScheduler scheduler = run.Mode == EGroupScheduleMode.Parallel
                ? new ParallelGroupScheduler(this, run)
                : new SerialGroupScheduler(this, run);
            episode = new Episode(run, scheduler);
            _episodes[group.SessionId] = episode;
        }

        // 把群壳登记成在跑：界面据此进外驱模式（停止按钮、打字即插话），列表也看得见它在跑。
        // using 在 finally 之后才释放——先摘掉这一波再报空闲，否则界面看到空闲时再点「继续」会被当成重复调用
        using IDisposable running = SessionManager.Instance.Running.BeginRun(group.SessionId);
        try
        {
            await episode.Scheduler.RunAsync(kickoff);
        }
        finally
        {
            lock (_locker) _episodes.Remove(group.SessionId);
            episode.Run.Dispose();
        }

        return true;
    }

    private Episode? EpisodeOf(string groupId)
    {
        lock (_locker) return _episodes.GetValueOrDefault(groupId);
    }

    // 场景与规矩在系统提示里（ADR 0048，见 GroupSceneSource），投递只带这一刻才成立的东西
    private string ComposeInput(GroupRun run, ChatSession member, string delivery, int deliveredFrom)
    {
        // 主持人位只在并行里起作用：串行本来就人人轮到，点名是多余的
        bool coldStart = run.Mode == EGroupScheduleMode.Parallel
                         && member.SessionId == run.Group.GroupHostSessionId
                         && GroupTranscript.HasUnaddressedUserPost(run.Group.History, deliveredFrom, RosterOf(run.Group));
        return coldStart ? delivery + "\n\n" + GroupTranscript.HostColdStartHint : delivery;
    }

    private async Task<bool> RunTurnAsync(ChatSession member, string input, CancellationToken cancellationToken)
    {
        try
        {
            ChatMessage delivery = new(ChatRole.User, input);
            ChatMessageAnnotations.MarkGroupDelivery(delivery);
            return await _runner.RunAsync(member, delivery, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            Log.Error($"Group member '{member.Title}' ({member.SessionId}) failed: {e}");
            return false;
        }
    }

    /// <summary>
    /// 记下一条成员发言，并在这一波里广播、唤醒。
    /// 模型常照着投递格式自加 [名字]: 前缀（场景说明要求不要加，但弱模型不听）：
    /// 不剥就落盘的话，单行发言会被 markdown 当链接引用定义整段吞掉（气泡空白、复制有字），
    /// 投递给别人时还会再包一层变双前缀。剥空说明他只剩前缀、实质一个字没说，不记这条
    /// </summary>
    /// <returns>广播与唤醒做完；这条没记为 null</returns>
    private Task? PostFromMember(ChatSession group, ChatSession member, string text)
    {
        string body = GroupTranscript.StripSpeakerPrefix(text.Trim(), member.CharacterData.CharacterName);
        if (string.IsNullOrWhiteSpace(body)) return null;

        ChatMessage post = group.CreateMessage(ChatRole.Assistant, body);
        post.AuthorName = member.CharacterData.CharacterName;
        ChatMessageAnnotations.MarkGroupPost(post, member.CharacterId, member.SessionId);
        int index = Append(group, post);

        return EpisodeOf(group.SessionId) is { } episode
            ? OnAppendedAsync(episode, post, index, member.SessionId)
            : Task.CompletedTask;
    }

    private async Task OnAppendedAsync(Episode episode, ChatMessage post, int index, string? authorSessionId)
    {
        GroupPostEvent posted = new(index, authorSessionId, post.Text);
        Task broadcast = episode.Run.EnqueueBroadcast(() => BroadcastAsync(episode, post, posted));
        episode.Scheduler.OnPosted(posted);
        try
        {
            await broadcast;
        }
        catch (Exception e)
        {
            Log.Warning($"Group broadcast failed: {e.Message}");
        }
    }

    private async Task BroadcastAsync(Episode episode, ChatMessage post, GroupPostEvent posted)
    {
        string body = GroupTranscript.StripSpeakerPrefix(post.Text.Trim(), post.AuthorName);
        if (body.Length == 0) return;

        string text = GroupTranscript.FormatPost(post.AuthorName, body);
        foreach (GroupMemberTurnState turn in episode.Run.Turns)
        {
            if (turn.Member.SessionId == posted.AuthorSessionId) continue; //发言不回投给发送者本人
            // 插不插由调度定：插进去就是让他多说一句，要守停止条件
            if (!episode.Scheduler.ShouldInject(posted, turn.Member.SessionId)) continue;
            await turn.InjectAsync(_runner, posted.Index, text);
        }
    }

    private int Append(ChatSession group, ChatMessage post)
    {
        lock (_locker)
        {
            int index = group.History.Count;
            group.History.Add(post);
            group.SaveAppended(index);
            return index;
        }
    }

    private sealed record Episode(GroupRun Run, IGroupScheduler Scheduler);
}
