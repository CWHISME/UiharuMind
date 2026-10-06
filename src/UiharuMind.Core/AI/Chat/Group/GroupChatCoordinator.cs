using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;
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
public sealed partial class GroupChatCoordinator : IGroupTurnHost
{
    private readonly IGroupMemberTurnRunner _runner;
    private readonly Func<string, ChatSession?> _load;
    private readonly Func<ChatSession, bool> _seesImages; //成员此刻的模型能不能看图：看得了才转交群里的图
    private readonly Func<TimeSpan, Task> _delay; //私聊后接回等一会：测试换成假的，由测试决定什么时候到点
    private readonly object _locker = new();
    private readonly Dictionary<string, Episode> _episodes = new(); //群 → 正在跑的那一波
    private readonly HashSet<string> _interrupted = new(); //群里那一轮被停或失败的成员：投递已交给他，没新话也要能接着做

    /// <summary>应用里的那一个：成员用无头编排跑，会话经会话管理器取</summary>
    public static GroupChatCoordinator Instance { get; } =
        new(new HeadlessGroupMemberTurnRunner(), id => SessionManager.Instance.Load(id));

    /// <summary>
    /// 构造一个调度器（测试用来换掉跑模型的那一段）
    /// </summary>
    /// <param name="runner">成员一轮的跑法</param>
    /// <param name="load">按标识取会话</param>
    /// <param name="seesImages">成员的模型能不能看图；null 按会话的有效模型判断</param>
    /// <param name="delay">私聊后接回的等待；null 走 <see cref="Task.Delay(TimeSpan)"/></param>
    public GroupChatCoordinator(IGroupMemberTurnRunner runner, Func<string, ChatSession?> load,
        Func<ChatSession, bool>? seesImages = null, Func<TimeSpan, Task>? delay = null)
    {
        _runner = runner;
        _load = load;
        _seesImages = seesImages ?? (member => member.ChatModelRunningData?.IsVisionModel == true);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// 群当前发言人变了（有人开口 / 有人说完）。参数是群壳会话标识。
    /// ⚠️ <b>可能来自后台线程</b>（成员一轮在无头编排上跑），订阅方自行 marshal。
    /// </summary>
    public event Action<string>? SpeakerChanged;

    /// <summary>
    /// 一波收场了，且群已报空闲：此时开下一波（<see cref="PostAsync"/> / <see cref="ContinueAsync"/>）不会被当成重复调用。
    /// ⚠️ <b>来自后台线程</b>，订阅方自行 marshal；订阅方抛出只记日志
    /// </summary>
    public event Action<GroupEpisodeSummary>? EpisodeEnded;

    /// <summary>
    /// 用户本人往群里说了一句（化身替用户说的不算），参数是群壳会话标识。离席据此取消正在走的倒计时。
    /// ⚠️ 订阅方抛出只记日志
    /// </summary>
    public event Action<string>? UserPosted;

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
    /// <param name="text">发言（附件的路径引用已拼在正文里）</param>
    /// <param name="images">随发言发出的图片；没有为 null</param>
    public Task PostAsync(ChatSession group, string text, IReadOnlyList<DataContent>? images = null)
    {
        // 先通报再落：这句开的一波可能当场跑完，离席在波末排上的新倒计时不能被这一声误取消
        try
        {
            UserPosted?.Invoke(group.SessionId);
        }
        catch (Exception e)
        {
            Log.Error($"Group user-posted handler failed: {e}");
        }

        return PostUserMessageAsync(group, group.CreateMessage(ChatRole.User, text, images));
    }

    /// <summary>
    /// 群流水此刻的一份拷贝（与追加同一把锁）：后台读群流水的地方用，免得边读边被追加
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>拷贝</returns>
    public IReadOnlyList<ChatMessage> HistorySnapshot(ChatSession group)
    {
        lock (_locker) return group.History.ToList();
    }

    // 用户发言（含化身替用户说的）落进群流水：群闲着就由它开一波，在跑就插进那一波
    private async Task PostUserMessageAsync(ChatSession group, ChatMessage post)
    {
        int index = Append(group, post);

        while (true)
        {
            if (EpisodeOf(group.SessionId) is { } running)
            {
                if (await OnAppendedAsync(running, post, index, null)) return;
                // 那一波已收场、只是还没摘：它接不住这句。等它摘掉，由这句开一波
                await running.Removed.Task;
                continue;
            }

            // 开波落空说明恰好别处先开了一波：回到上面，按插进那一波处理
            if (await RunEpisodeAsync(group, new GroupKickoff(index))) return;
        }
    }

    /// <summary>
    /// 不开口也让大家接着说：串行再跑一圈；并行叫醒所有还有新话没听的成员。一个群同时只跑一波，重复调用是空操作
    /// </summary>
    /// <param name="group">群壳会话</param>
    public async Task ContinueAsync(ChatSession group)
    {
        while (!await RunEpisodeAsync(group, new GroupKickoff(null)))
        {
            if (EpisodeOf(group.SessionId) is not { } running) continue;
            if (!running.Scheduler.IsFinished) return; //还在跑：重复调用
            await running.Removed.Task; //已收场、只是还没摘：等它摘掉再开，不然这一下「继续」就静默落空
        }
    }

    /// <summary>
    /// 改名单：与开波同一把锁，群在跑一波就不改。调度边跑边读名单，
    /// 「先查空闲、再改」两步之间可能恰好被一句发言开出一波，所以判断与修改必须在一把锁里做完
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="edit">改名单（整份替换 <see cref="ChatSession.GroupMemberSessionIds"/>）及随之的落盘</param>
    /// <returns>改了为 true；群正在跑一波为 false</returns>
    public bool TryEditRoster(ChatSession group, Action edit)
    {
        lock (_locker)
        {
            if (_episodes.ContainsKey(group.SessionId)) return false;
            edit();
            return true;
        }
    }

    /// <summary>
    /// 往名单末尾加人：群在跑也能加。只追加、整份替换，与取投递同一把锁——调度读到的不是加之前的整份就是加之后的整份；
    /// 正在说的人这一轮还不知道新人，下一轮按新场景段重建装配才知道
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="edit">加人（整份替换 <see cref="ChatSession.GroupMemberSessionIds"/>）及随之的落盘</param>
    public void AddToRoster(ChatSession group, Action edit)
    {
        lock (_locker) edit();
    }

    /// <summary>
    /// 成员退群：忘掉他「被打断待续」的记号与待交的附注，不然加回来后第一轮会先收到这些
    /// </summary>
    /// <param name="memberSessionId">成员会话标识</param>
    public void ForgetMember(string memberSessionId)
    {
        lock (_locker)
        {
            _interrupted.Remove(memberSessionId);
            _preempted.Remove(memberSessionId);
            _notes.Remove(memberSessionId);
        }
    }

    /// <summary>
    /// 停下这个群正在跑的那一波（连同所有正在说的成员），以及正在跑的化身那一轮。
    /// 被私聊叫停、正等着接回的成员也不再自动接回：用户要群停下，留给「继续」
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    public void Stop(string groupId)
    {
        EpisodeOf(groupId)?.Run.Cancel();
        StopAvatar(groupId);
        ForgetResumes(groupId);
    }

    /// <summary>
    /// 智能体形态的成员经群发言工具说一句：这是它在群里唯一的发言通道（ADR 0060）。调用方不是群成员时返回 false
    /// </summary>
    /// <param name="memberSessionId">发言人的会话标识</param>
    /// <param name="content">发言</param>
    /// <returns>发出去了为 true</returns>
    public bool TryPostFromMember(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (_load(memberSessionId) is not { IsGroupMember: true } member) return false;
        if (_load(member.GroupId!) is not { IsGroup: true } group) return false;
        if (!group.GroupMemberSessionIds.Contains(member.SessionId)) return false; //已退群

        // 广播与唤醒不等：工具调用要立刻拿到回执，那一段在后台照追加顺序做完
        return PostFromMember(group, member, content.Trim()) != null;
    }

    async Task<GroupTurnOutcome> IGroupTurnHost.RunMemberAsync(GroupRun run, string memberSessionId,
        EGroupWakeCause cause)
    {
        GroupTurnOutcome outcome = await RunMemberTurnAsync(run, memberSessionId, cause);
        // 闸放了才约：被私聊叫停的（或接回时又撞上私聊的），私聊结束后叫醒他接着做
        ArmResume(run.Group, memberSessionId);
        return outcome;
    }

    private async Task<GroupTurnOutcome> RunMemberTurnAsync(GroupRun run, string memberSessionId,
        EGroupWakeCause cause)
    {
        ChatSession group = run.Group;
        if (_load(memberSessionId) is not { } member) return GroupTurnOutcome.Skipped;

        // 成员此刻正在私聊（前台轮占着闸）：这次跳过，游标不动，下次补投——不丢话。
        // 跑着时用户私聊他：只叫停他这一轮，别人照常说（ADR 0063）
        using PreemptibleTurn? gate = GroupMemberTurnGate.TryEnterPreemptible(member.SessionId, run.Token);
        if (gate == null) return GroupTurnOutcome.Skipped;

        string? delivery;
        bool resumeAfterPrivate;
        IReadOnlyList<DataContent> images;
        DeliveryTaken taken;
        GroupMemberTurnState? turn = null;
        // 取投递、推游标、登记在说，与 Append 同一把锁：之后追加的每一条要么在投递里、要么会插给他，不漏不重
        lock (_locker)
        {
            HashSet<int> consumed = member.GroupConsumedPosts;
            member.GroupConsumedPosts = [];
            taken = new DeliveryTaken(member.GroupCursor, consumed, member.GroupBriefing);
            delivery = GroupTranscript.BuildDelivery(group.History, member.GroupCursor, member.SessionId, consumed);
            // 被私聊叫停的：有没有新话都交代一声，那之后隔着私聊，光看历史他会只接新话、忘了手上的活。
            // 投递留空串照样开口，提示由 ComposeInput 贴在末尾
            resumeAfterPrivate = _preempted.Remove(member.SessionId);
            if (resumeAfterPrivate) delivery ??= string.Empty;
            // 被打断的人游标早推过去了，没新话时交一句「接着做」；有新话就照常投，那一段历史他自己看得见
            if (_interrupted.Remove(member.SessionId)) delivery ??= GroupTranscript.ResumeNote;
            // 附注不管有没有新话都要交（见 NotifyMemberAsync），排在新话之前
            delivery = TakeNotes(member.SessionId, delivery, out List<string>? notes);
            taken = taken with { Notes = notes };
            images = GroupTranscript.DeliveryImages(group.History, member.GroupCursor, member.SessionId, consumed);
            member.GroupCursor = group.History.Count;
            // 入群摘要只搭投递的车：没新话时不为它单独叫醒
            if (delivery != null && member.GroupBriefing is { } briefing)
            {
                delivery = briefing + "\n\n" + delivery;
                member.GroupBriefing = null;
            }
            if (delivery != null)
            {
                turn = new GroupMemberTurnState(member, cause, group.History.Count);
                run.Begin(turn);
            }
        }

        if (turn == null) return GroupTurnOutcome.Skipped; //没有新话可接，这次他不开口

        bool closed = false;
        try
        {
            // 锁外通报：订阅方会回来问 IsRunning/SpeakersOf
            SpeakerChanged?.Invoke(group.SessionId);

            string input = ComposeInput(member, cause, delivery!, resumeAfterPrivate);
            // 回复就是发言（ADR 0060 修订）：插话会让一轮说好几次话，每次说完都进群，而不是只取最后一条；
            // 智能体形态另有群发言工具管中途说话，回复照样进群——不看这一轮调没调过工具
            using GroupMemberReplyFeed replies = new(member, (text, at) => PostReply(group, member, text, at));
            // 图的路径引用在正文里，看不了图的成员靠它用识图工具；看得了的直接给图
            ChatMessage deliveryMessage = GroupTranscript.DeliveryMessage(input, _seesImages(member) ? images : []);
            // 并行里说完即封口：别人的话不再让他续说一句，要不要再开口交给唤醒边界（ADR 0049 修订）。
            // 串行不封：本来人人轮到，没有谁一直被续着说
            Func<Task>? seal = run.Mode == EGroupScheduleMode.Parallel ? () => turn.SealAsync(_runner) : null;
            bool completed = await RunTurnAsync(member, deliveryMessage, seal, gate.Token);
            replies.Finish(completed);
            // 装配阶段就被停下：投递根本没进他的历史，退回去下次照常交，不然游标前那几条他再也看不到
            bool undelivered = !completed && !member.History.Any(x => ReferenceEquals(x, deliveryMessage));
            if (!completed)
            {
                lock (_locker)
                {
                    _interrupted.Add(member.SessionId);
                    if (gate.WasPreempted(completed)) _preempted.Add(member.SessionId);
                }
            }

            IReadOnlySet<int> consumedNow = await turn.CloseAsync(_runner);
            closed = true;
            lock (_locker)
            {
                if (undelivered) Restore(member, taken, consumedNow);
                else member.GroupConsumedPosts = [..consumedNow];
            }
            member.SaveMeta(touchUpdatedAt: false);

            if (run.End(member.SessionId)) SpeakerChanged?.Invoke(group.SessionId);
            await replies.WhenPostedAsync();

            // 失败或被停：已经说完的几段照常算，没说完的半截留在他自己的会话里
            return new GroupTurnOutcome(completed, consumedNow);
        }
        finally
        {
            // 兜中途抛出：登记了「在说」就一定摘掉，否则这一波里他一直算在说、广播还往一个没人消费的轮次里插。
            // 已插进去的先撤回（它们没被消费，游标之后照常投递），不然他下一轮会连同新投递一起冒出来
            if (!closed) await turn.CloseAsync(_runner);
            if (run.End(member.SessionId)) SpeakerChanged?.Invoke(group.SessionId);
        }
    }

    bool IGroupTurnHost.HasNewLines(ChatSession group, string memberSessionId)
    {
        if (_load(memberSessionId) is not { } member) return false;

        lock (_locker)
        {
            if (_interrupted.Contains(memberSessionId)) return true; //「继续」要叫得醒被打断的人
            if (_notes.ContainsKey(memberSessionId)) return true; //有附注待交
            return GroupTranscript.BuildDelivery(group.History, member.GroupCursor, memberSessionId,
                member.GroupConsumedPosts) != null;
        }
    }

    /// <inheritdoc cref="IGroupTurnHost.RosterOf" />
    /// 走注入的 <c>_load</c>（测试拿临时会话喂，它们不在索引里）
    public IReadOnlyList<GroupRosterEntry> RosterOf(ChatSession group) =>
        GroupRoster.Of(group, _load).Present.Select(x => new GroupRosterEntry(x.SessionId, x.Name)).ToList();

    private async Task<bool> RunEpisodeAsync(ChatSession group, GroupKickoff kickoff)
    {
        Episode episode;
        int start;
        lock (_locker)
        {
            if (_episodes.ContainsKey(group.SessionId)) return false;
            start = kickoff.UserPostIndex ?? group.History.Count;

            GroupRun run = new(group);
            IGroupScheduler scheduler = run.Mode == EGroupScheduleMode.Parallel
                ? new ParallelGroupScheduler(this, run)
                : new SerialGroupScheduler(this, run);
            episode = new Episode(run, scheduler);
            _episodes[group.SessionId] = episode;
        }

        bool stopped;
        int end;
        // 把群壳登记成在跑：界面据此进外驱模式（停止按钮、打字即插话），列表也看得见它在跑。
        // 先摘掉这一波再报空闲，否则界面看到空闲时再点「继续」会被当成重复调用
        using (SessionManager.Instance.Running.BeginRun(group.SessionId))
        {
            try
            {
                await episode.Scheduler.RunAsync(kickoff);
            }
            finally
            {
                lock (_locker)
                {
                    _episodes.Remove(group.SessionId);
                    end = group.History.Count;
                }
                stopped = episode.Run.Token.IsCancellationRequested;
                episode.Run.Dispose();
                episode.Removed.TrySetResult();
            }
        }

        // 报空闲之后再通报：订阅方（离席）可能当场开下一波。
        // 单为一人开的一波（交附注、私聊后接回）若一句没说（他正在私聊被闸挡住），不算一波：离席会把它记成「没进展」
        if (kickoff.OnlyMemberSessionId == null || end > start)
            RaiseEpisodeEnded(new GroupEpisodeSummary(group, kickoff.UserPostIndex, start, end, stopped));
        return true;
    }

    private void RaiseEpisodeEnded(GroupEpisodeSummary summary)
    {
        try
        {
            EpisodeEnded?.Invoke(summary);
        }
        catch (Exception e)
        {
            Log.Error($"Group episode-ended handler failed: {e}");
        }
    }

    private Episode? EpisodeOf(string groupId)
    {
        lock (_locker) return _episodes.GetValueOrDefault(groupId);
    }

    // 场景与规矩在系统提示里（ADR 0048，见 GroupSceneSource），投递只带这一刻才成立的东西。
    // 重锚之后的提示界面不画
    private string ComposeInput(ChatSession member, EGroupWakeCause cause, string delivery, bool resumeAfterPrivate)
    {
        string input = delivery + "\n\n" + GroupTranscript.VoiceReminder(member.CharacterData.GetPersonaCoda());
        if (resumeAfterPrivate)
            input += "\n\n" + GroupTranscript.PrivateReplyChannelNote + "\n\n" + GroupTranscript.PrivateResumeNote;
        if (cause == EGroupWakeCause.CatchUp) input += "\n\n" + GroupTranscript.CatchUpHint;
        return input;
    }

    // 投递没交出去：游标、已读、入群摘要、附注都退回取投递之前。调用方持 _locker
    private void Restore(ChatSession member, DeliveryTaken taken, IReadOnlySet<int> consumedNow)
    {
        member.GroupCursor = taken.Cursor;
        member.GroupConsumedPosts = [..taken.Consumed, ..consumedNow];
        member.GroupBriefing ??= taken.Briefing;
        if (taken.Notes != null) RestoreNotes(member.SessionId, taken.Notes);
    }

    private async Task<bool> RunTurnAsync(ChatSession member, ChatMessage delivery, Func<Task>? onReplyFinishing,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(member, delivery, onReplyFinishing, cancellationToken);
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
    /// 投递给别人时还会再包一层变双前缀。剥空说明他只剩前缀、实质一个字没说，不记这条。
    /// 群里这条沿用成员那条消息的时间（回复进群走这条）：同一句话两边是同一时刻，
    /// 不因「落盘与追加之间隔了几秒」在分钟精度上错开一位
    /// </summary>
    /// <returns>广播与唤醒做完；这条没记为 null</returns>
    private Task? PostFromMember(ChatSession group, ChatSession member, string text,
        DateTimeOffset? createdAt = null)
    {
        string body = GroupTranscript.StripSpeakerPrefix(text.Trim(), member.CharacterData.CharacterName);
        if (string.IsNullOrWhiteSpace(body)) return null;

        ChatMessage post = group.CreateMessage(ChatRole.Assistant, body, createdAt: createdAt);
        post.AuthorName = member.CharacterData.CharacterName;
        ChatMessageAnnotations.MarkGroupPost(post, member.CharacterId, member.SessionId);
        int index = Append(group, post);

        return EpisodeOf(group.SessionId) is { } episode
            ? OnAppendedAsync(episode, post, index, member.SessionId)
            : Task.CompletedTask;
    }

    // 成员说完的一段：回「[沉默]」就是这次不接话，不进群，也就不广播、不叫醒谁
    private Task? PostReply(ChatSession group, ChatSession member, string text, DateTimeOffset? createdAt)
    {
        string own = GroupTranscript.StripSpeakerPrefix(text, GroupSceneSource.SpeakerNameOf(member));
        return GroupTranscript.IsPass(own) ? null : PostFromMember(group, member, text, createdAt);
    }

    // 返回这一波接没接住：已收场的波接不住，用户发言由调用方另开一波；成员发言照旧只落盘（没有波时也是如此）
    private async Task<bool> OnAppendedAsync(Episode episode, ChatMessage post, int index, string? authorSessionId)
    {
        GroupPostEvent posted = new(index, authorSessionId, post.Text);
        Task broadcast = episode.Run.EnqueueBroadcast(() => BroadcastAsync(episode, post, posted));
        bool accepted = episode.Scheduler.OnPosted(posted);
        try
        {
            await broadcast;
        }
        catch (Exception e)
        {
            Log.Warning($"Group broadcast failed: {e.Message}");
        }

        return accepted;
    }

    private async Task BroadcastAsync(Episode episode, ChatMessage post, GroupPostEvent posted)
    {
        string body = GroupTranscript.StripSpeakerPrefix(post.Text.Trim(), post.AuthorName);
        if (body.Length == 0) return;

        string text = GroupTranscript.FormatInjection(post.AuthorName, body);
        foreach (GroupMemberTurnState turn in episode.Run.Turns)
        {
            // 发言不回投给发送者本人。其余在跑的人都插：他们要看得到同伴实时说了什么（ADR 0049 第 12 条已撤）。
            // 没什么可接的由他回「[跳过]」收住，不靠这里挡
            if (turn.Member.SessionId == posted.AuthorSessionId) continue;
            await turn.InjectAsync(_runner, posted.Index, text,
                _seesImages(turn.Member) ? GroupTranscript.ImagesOf(post) : []);
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

    // 取投递之前的样子：投递没交出去时照它退回
    private readonly record struct DeliveryTaken(int Cursor, HashSet<int> Consumed, string? Briefing,
        List<string>? Notes = null);

    private sealed record Episode(GroupRun Run, IGroupScheduler Scheduler)
    {
        /// <summary>这一波已从登记处摘掉：等它的人（撞上已收场的波）此后可以另开一波</summary>
        public TaskCompletionSource Removed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
