using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群聊的调度（ADR 0046）：用户发言 → 成员按顺序一人一轮，<b>一圈即停</b>（决策 5）。
///
/// 投递两种从一开始都做（决策 3）：成员闲着时，轮到他再把游标之后的新发言合成一条交给他；
/// 他正在跑时，走注入队列在安全点插进去。换成并行只改调度，不改投递。
///
/// 群流水的<b>唯一写入口</b>在这里：用户发言、成员正文、成员用 SendMessage 发的都经 <see cref="Append"/>，
/// 于是「送达即不可改」（决策 6）与只追加的顺序由构造保证。
/// </summary>
public sealed class GroupChatCoordinator
{
    private readonly IGroupMemberTurnRunner _runner;
    private readonly Func<string, ChatSession?> _load;
    private readonly object _locker = new();
    private readonly Dictionary<string, CancellationTokenSource> _rounds = new(); //群 → 正在跑的那一圈
    private readonly Dictionary<string, string> _speakers = new(); //群 → 正在发言的成员会话
    private readonly Dictionary<string, HashSet<int>> _injected = new(); //成员会话 → 已插话插给他的群流水下标

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
    /// 认不认得出「对全群说」。SendMessage 的 to 写这几个就是发群，不是找某个人
    /// </summary>
    /// <param name="to">收件人</param>
    /// <returns>是发群为 true</returns>
    public static bool IsGroupAddress(string? to) =>
        string.Equals(to?.Trim(), "group", StringComparison.OrdinalIgnoreCase) || to?.Trim() is "群" or "全群" or "群里";

    /// <summary>这个群此刻是不是在跑一圈</summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>在跑为 true</returns>
    public bool IsRunning(string groupId)
    {
        lock (_locker) return _rounds.ContainsKey(groupId);
    }

    /// <summary>
    /// 用户往群里发言。群闲着就开跑一圈；正在跑就插进当前发言人的这一轮，
    /// 其余成员轮到时照常收到（插不进去的，当前发言人下一轮也会收到——游标只在投递时前进）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="text">发言</param>
    public async Task PostAsync(ChatSession group, string text)
    {
        ChatMessage post = group.CreateMessage(ChatRole.User, text);
        int index = Append(group, post);

        if (IsRunning(group.SessionId))
        {
            await TryInjectIntoSpeakerAsync(group, post, index);
            return;
        }

        await RunRoundAsync(group);
    }

    /// <summary>
    /// 跑一圈：成员按顺序各跑一轮，没有新话可接的跳过。一个群同时只跑一圈，重复调用是空操作
    /// </summary>
    /// <param name="group">群壳会话</param>
    public async Task RunRoundAsync(ChatSession group)
    {
        CancellationTokenSource cancellation;
        lock (_locker)
        {
            if (_rounds.ContainsKey(group.SessionId)) return;
            cancellation = new CancellationTokenSource();
            _rounds[group.SessionId] = cancellation;
        }

        // 把群壳登记成在跑：界面据此进外驱模式（停止按钮、打字即插话），列表也看得见它在跑。
        // using 在 finally 之后才释放——先摘掉 _rounds 再报空闲，否则界面看到空闲时再点「继续」会被当成重复调用
        using IDisposable run = SessionManager.Instance.Running.BeginRun(group.SessionId);
        try
        {
            foreach (string memberId in group.GroupMemberSessionIds.ToList())
            {
                if (cancellation.IsCancellationRequested) break;
                if (_load(memberId) is not { } member) continue;

                await RunMemberAsync(group, member, cancellation.Token);
            }
        }
        finally
        {
            lock (_locker) _rounds.Remove(group.SessionId);
            cancellation.Dispose();
        }
    }

    /// <summary>停下这个群正在跑的那一圈（连同当前发言人的这一轮）</summary>
    /// <param name="groupId">群壳会话标识</param>
    public void Stop(string groupId)
    {
        lock (_locker)
        {
            if (_rounds.TryGetValue(groupId, out CancellationTokenSource? cancellation)) cancellation.Cancel();
        }
    }

    /// <summary>
    /// 成员自己用 SendMessage 往群里发一句（决策 4）。调用方不是群成员时返回 false，交回委派那条路
    /// </summary>
    /// <param name="memberSessionId">发言人的会话标识</param>
    /// <param name="content">发言</param>
    /// <returns>发出去了为 true</returns>
    public bool TryPostFromMember(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (_load(memberSessionId) is not { IsGroupMember: true } member) return false;
        if (_load(member.GroupId!) is not { IsGroup: true } group) return false;

        AppendMemberPost(group, member, content.Trim());
        return true;
    }

    private async Task RunMemberAsync(ChatSession group, ChatSession member, CancellationToken cancellationToken)
    {
        bool firstDelivery = member.GroupCursor == 0 && member.History.Count == 0;
        string? delivery;
        lock (_locker)
        {
            _injected.TryGetValue(member.SessionId, out HashSet<int>? injected);
            delivery = GroupTranscript.BuildDelivery(group.History, member.GroupCursor, member.SessionId, injected);
            member.GroupCursor = group.History.Count;
            _injected.Remove(member.SessionId);
            if (delivery != null) _speakers[group.SessionId] = member.SessionId;
        }

        // 工作区以群壳为准,每轮开跑前对齐:右栏改的是群的工作区,成员各存一份就会对不上
        member.WorkspacePath = group.IsAgentGroup && member.CharacterData.IsAgent ? group.WorkspacePath : null;
        member.SaveMeta(touchUpdatedAt: false);
        if (delivery == null) return; //没有新话可接,这一圈他不开口

        if (firstDelivery) delivery = BuildScene(group, member) + "\n\n" + delivery;

        int logBefore = group.History.Count;
        int historyBefore = member.History.Count;
        bool completed;
        try
        {
            completed = await _runner.RunAsync(member, new ChatMessage(ChatRole.User, delivery), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            completed = false;
        }
        catch (Exception e)
        {
            Log.Error($"Group member '{member.Title}' ({member.SessionId}) failed: {e}");
            completed = false;
        }
        finally
        {
            lock (_locker) _speakers.Remove(group.SessionId);
        }

        // 失败或被停：半截输出不算群发言，它留在他自己的会话里
        if (!completed) return;
        if (GroupTranscript.PostedSince(group.History, logBefore, member.SessionId)) return; //自己用 SendMessage 发过了

        string? text = GroupTranscript.PickFinalText(member.History, historyBefore);
        if (text != null) AppendMemberPost(group, member, text);
    }

    private async Task TryInjectIntoSpeakerAsync(ChatSession group, ChatMessage post, int index)
    {
        string? speakerId;
        lock (_locker) _speakers.TryGetValue(group.SessionId, out speakerId);
        if (speakerId == null || _load(speakerId) is not { } speaker) return;

        ChatMessage interjection = new(ChatRole.User, GroupTranscript.FormatPost(post.AuthorName, post.Text.Trim()));
        if (!await _runner.TryInjectAsync(speaker, interjection)) return;

        lock (_locker)
        {
            if (!_injected.TryGetValue(speakerId, out HashSet<int>? injected))
            {
                injected = [];
                _injected[speakerId] = injected;
            }

            injected.Add(index);
        }
    }

    private string BuildScene(ChatSession group, ChatSession member)
    {
        List<string> others = group.GroupMemberSessionIds
            .Where(x => x != member.SessionId)
            .Select(x => _load(x)?.CharacterData.CharacterName)
            .OfType<string>()
            .ToList();
        CharacterData self = member.CharacterData;
        bool canPostMidTurn = self.IsAgent && self.Tools.EnableSubAgent;
        return GroupTranscript.BuildScene(group.Title, self.CharacterName, others,
            CharacterManager.Instance.UserCharacterName, canPostMidTurn);
    }

    private void AppendMemberPost(ChatSession group, ChatSession member, string text)
    {
        ChatMessage post = group.CreateMessage(ChatRole.Assistant, text);
        post.AuthorName = member.CharacterData.CharacterName;
        ChatMessageAnnotations.MarkGroupPost(post, member.CharacterId, member.SessionId);
        Append(group, post);
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
}
