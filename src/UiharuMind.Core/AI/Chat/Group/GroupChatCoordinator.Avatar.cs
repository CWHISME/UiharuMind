using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.AI.Chat.Group.Away;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>化身调群发言工具（SendMessage）的结果：回执按原因给文案，别让模型猜错在哪一步</summary>
public enum EAvatarPostResult
{
    /// <summary>已以用户名义发到群里</summary>
    Posted,

    /// <summary>会话标识不是化身、或没挂在群壳上</summary>
    InvalidSession,

    /// <summary>化身此刻没有轮在跑：工具调不到它</summary>
    NotInTurn,

    /// <summary>本轮已调结束离席：结束之后的话不进群</summary>
    AwayEnded,

    /// <summary>那一轮恰被停（已从运行登记摘掉）：不代发，别让一句迟到的话绕过停止</summary>
    TurnStopped,
}

// 化身那一轮（ADR 0055）：与成员共用投递、游标与群流水写入，但它说完的话以用户的名义进群、由它开下一波。
// 化身唯一发群通道是 SendMessage 工具（<see cref="GroupPostTool"/>），回复正文只留它自己的会话，不再自动进群。
// 字段 _avatarTurns 声明在这一份里：只有这一面用它
public sealed partial class GroupChatCoordinator
{
    // 群 → 正在跑的化身那一轮：Token 供停止；Start 供「已调结束就不再接受发群」检查；EndCallsBlocked 供 FindEnd
    private readonly Dictionary<string, AvatarTurn> _avatarTurns = new();

    private sealed record AvatarTurn(CancellationTokenSource Token, int Start, bool EndCallsBlocked);

    /// <summary>
    /// 化身经群发言工具（SendMessage）说一句（化身唯一的发群通道）：以用户名义进群、开一波，
    /// 与成员的 <see cref="TryPostFromMember"/> 各走各的——化身不在成员名单里，不能走那条
    /// </summary>
    /// <param name="avatarSessionId">化身会话标识</param>
    /// <param name="content">要发的话</param>
    /// <returns>结果（<see cref="EAvatarPostResult.Posted"/> 为发出去了）</returns>
    public EAvatarPostResult TryPostFromAvatar(string avatarSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return EAvatarPostResult.InvalidSession;
        if (_load(avatarSessionId) is not { IsGroupAvatar: true } avatar) return EAvatarPostResult.InvalidSession;
        if (_load(avatar.GroupId!) is not { IsGroup: true } group) return EAvatarPostResult.InvalidSession;

        AvatarTurn? context;
        lock (_locker) context = _avatarTurns.GetValueOrDefault(group.SessionId);
        if (context == null) return EAvatarPostResult.NotInTurn; //化身此刻没有轮在跑：工具调不到它

        // 已调结束离席：不再接受发言（与旧的自动进群 PostFromAvatar 同一口径——结束之后的话不进群）
        if (GroupAvatarTurn.FindEnd(avatar.History.Skip(context.Start), context.EndCallsBlocked) != null)
            return EAvatarPostResult.AwayEnded;

        // 一轮恰被 Stop 摘掉（轮收尾 Remove 与工具执行竞态）：不再代发，别让一句迟到的话绕过停止再开一波
        lock (_locker)
        {
            if (!_avatarTurns.ContainsKey(group.SessionId)) return EAvatarPostResult.TurnStopped;
        }

        return PostAsAvatar(group, avatar, content.Trim()) != null
            ? EAvatarPostResult.Posted
            : EAvatarPostResult.InvalidSession; //剥完前缀没正文（理论上 Post 已挡空串，防御）
    }

    // 化身以用户名义发群：SendMessage 工具的唯一落点（只经 SendMessage）。剥前缀、空话不发、署用户名义
    // （MarkGroupAvatarPost）、经 PostUserMessageAsync 落盘并开一波
    private Task? PostAsAvatar(ChatSession group, ChatSession avatar, string text, DateTimeOffset? createdAt = null)
    {
        string body = GroupTranscript.StripSpeakerPrefix(text.Trim(), GroupSceneSource.SpeakerNameOf(avatar));
        if (string.IsNullOrWhiteSpace(body)) return null;

        ChatMessage post = group.CreateMessage(ChatRole.User, body, imageContents: null, createdAt);
        ChatMessageAnnotations.MarkGroupAvatarPost(post, avatar.SessionId);
        PostUserMessageAsync(group, post).LogOnFault("post for the group avatar");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 这个群的化身此刻在不在跑
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>在跑为 true</returns>
    public bool IsAvatarRunning(string groupId)
    {
        lock (_locker) return _avatarTurns.ContainsKey(groupId);
    }

    /// <summary>
    /// 让化身跑一轮：投递它还没听过的群发言（可附一句这一刻的提示）。
    /// 它要说话只能调 SendMessage（<see cref="TryPostFromAvatar"/>），回复正文只留它自己的会话；
    /// 不发话由离席控制器按没进展退避再唤（ADR 0055 修订）。
    /// 群闲着就开一波、在跑就插进那一波。不等那一波跑完：波末会再叫醒它
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="avatar">化身会话</param>
    /// <param name="note">附在投递末尾的提示（上次没进展的情况）；没有为 null</param>
    /// <param name="cancellationToken">离席结束时取消</param>
    /// <param name="endCallsBlocked">结束调用是不是被拦着（无限模式）：调了也只拿到错误，不结束</param>
    /// <param name="deliveryOverride">化身的投递正文；非空时代替合成群发言段（离席的第三方视角：只给锚点、不灌正文）</param>
    /// <returns>这一轮的结局</returns>
    public async Task<GroupAvatarTurn> RunAvatarAsync(ChatSession group, ChatSession avatar, string? note,
        CancellationToken cancellationToken, bool endCallsBlocked = false, string? deliveryOverride = null)
    {
        using CancellationTokenSource turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 用户正在私聊化身：这次不跑，游标不动。跑着时用户私聊它：叫停这一轮，由离席在私聊结束后接回（ADR 0063）
        using PreemptibleTurn? gate = GroupMemberTurnGate.TryEnterPreemptible(avatar.SessionId, turn.Token);
        if (gate == null) return GroupAvatarTurn.Busy;
        string? delivery;
        int cursor;
        int postsAtStart;
        lock (_locker)
        {
            if (!_avatarTurns.TryAdd(group.SessionId, new AvatarTurn(turn, avatar.History.Count, endCallsBlocked)))
                return GroupAvatarTurn.Busy;
            cursor = avatar.GroupCursor;
            delivery = deliveryOverride ?? GroupTranscript.BuildDelivery(group.History, cursor, avatar.SessionId);
            avatar.GroupCursor = group.History.Count;
            // posted 改轮末数群流水里的化身发言增量：SendMessage 工具那条路径也经 PostAsAvatar，落盘即计数
            postsAtStart = group.History.Count(x => ChatMessageAnnotations.GroupAvatarPostOf(x) == avatar.SessionId);
        }

        // 界面据此显示群在忙、给出停止按钮；用户这时发言照常开一波
        using IDisposable running = SessionManager.Instance.Running.BeginRun(group.SessionId);
        int start = avatar.History.Count;
        try
        {
            string input = delivery ?? GroupAvatarTranscript.NothingNew;
            if (note != null) input += "\n\n" + note;

            ChatMessage deliveryMessage = GroupTranscript.DeliveryMessage(input, []);
            bool completed = await RunTurnAsync(avatar, deliveryMessage, null, gate.Token);
            // 装配阶段就被停下：投递没进它的历史，游标退回去下次照常交
            if (!completed && !avatar.History.Any(x => ReferenceEquals(x, deliveryMessage)))
            {
                lock (_locker) avatar.GroupCursor = cursor;
            }

            avatar.SaveMeta(touchUpdatedAt: false);

            // 轮末计数与群流水 Append 同锁枚举：化身 Push 开的成员波可能正并发写群流水，锁外读裸 List 会撞版本检查
            int posted;
            lock (_locker)
            {
                posted = group.History.Count(x => ChatMessageAnnotations.GroupAvatarPostOf(x) == avatar.SessionId)
                         - postsAtStart;
            }

            // 私聊即正常消息：化身跑着时用户私聊走插话、闲着时私聊轮占闸叫离席轮 Busy，不再有抢占，preempted 恒 false
            return GroupAvatarTurn.Classify(avatar.History.Skip(start).ToList(), completed,
                turn.IsCancellationRequested && !cancellationToken.IsCancellationRequested, posted, endCallsBlocked,
                preempted: false);
        }
        finally
        {
            lock (_locker) _avatarTurns.Remove(group.SessionId);
        }
    }

    /// <summary>
    /// 往群流水里记一条离席回执：只给人看，不广播、不叫醒谁，投递永远跳过它
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="text">正文（化身的交代，界面另按回执画卡）</param>
    /// <param name="receiptJson">回执 JSON</param>
    public void AppendAwayReceipt(ChatSession group, string text, string receiptJson)
    {
        ChatMessage note = group.CreateMessage(ChatRole.Assistant, text, imageContents: null);
        ChatMessageAnnotations.MarkGroupAwayReceipt(note, receiptJson);
        Append(group, note);
    }

    private void StopAvatar(string groupId)
    {
        lock (_locker) _avatarTurns.GetValueOrDefault(groupId)?.Token.Cancel();
    }
}
