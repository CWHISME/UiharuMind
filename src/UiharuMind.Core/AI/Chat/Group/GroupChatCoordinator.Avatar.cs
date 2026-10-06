using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Group;

// 化身那一轮（ADR 0055）：与成员共用投递、游标与群流水写入，但它说完的话以用户的名义进群、由它开下一波。
// 字段 _avatarTurns 声明在这一份里：只有这一面用它
public sealed partial class GroupChatCoordinator
{
    private readonly Dictionary<string, CancellationTokenSource> _avatarTurns = new(); //群 → 正在跑的化身那一轮

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
    /// 让化身跑一轮：投递它还没听过的群发言（可附一句这一刻的提示），它每次说完的正文以用户的名义进群——
    /// 群闲着就开一波、在跑就插进那一波。不等那一波跑完：波末会再叫醒它
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="avatar">化身会话</param>
    /// <param name="note">附在投递末尾的提示（上次没进展的情况）；没有为 null</param>
    /// <param name="cancellationToken">离席结束时取消</param>
    /// <param name="endCallsBlocked">结束调用是不是被拦着（无限模式）：调了也只拿到错误，不结束、不吞掉它说的话</param>
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
        lock (_locker)
        {
            if (!_avatarTurns.TryAdd(group.SessionId, turn)) return GroupAvatarTurn.Busy;
            cursor = avatar.GroupCursor;
            delivery = deliveryOverride ?? GroupTranscript.BuildDelivery(group.History, cursor, avatar.SessionId);
            avatar.GroupCursor = group.History.Count;
        }

        // 界面据此显示群在忙、给出停止按钮；用户这时发言照常开一波
        using IDisposable running = SessionManager.Instance.Running.BeginRun(group.SessionId);
        int start = avatar.History.Count;
        int posted = 0;
        try
        {
            string input = delivery ?? GroupAvatarTranscript.NothingNew;
            if (note != null) input += "\n\n" + note;

            Task? PostFromAvatar(string text, DateTimeOffset? createdAt)
            {
                // 被停之后、调了结束离席之后再说的话都不进群：不让一句迟到的话绕过停止再开一波。
                // 无限模式里结束调用只会拿到错误，不拦它的话
                if (gate.Token.IsCancellationRequested) return null;
                if (GroupAvatarTurn.FindEnd(avatar.History.Skip(start), endCallsBlocked) != null) return null;
                return PostBody(text, createdAt);
            }

            Task? PostBody(string text, DateTimeOffset? createdAt)
            {
                string body = GroupTranscript.StripSpeakerPrefix(text.Trim(), GroupSceneSource.SpeakerNameOf(avatar));
                if (string.IsNullOrWhiteSpace(body)) return null;

                ChatMessage post = group.CreateMessage(ChatRole.User, body, imageContents: null, createdAt);
                ChatMessageAnnotations.MarkGroupAvatarPost(post, avatar.SessionId);
                Interlocked.Increment(ref posted);
                // 不等：这句开的那一波跑完要好一阵，波末会再叫醒化身
                PostUserMessageAsync(group, post).LogOnFault("post for the group avatar");
                return Task.CompletedTask;
            }

            // 结束调用里 say 参数带的告别话：和调用写在同一条消息里，回复流按"带工具调用的不算说完"跳过，
            // 但它是点名要进群的——调用边上的顺手正文照样只留在本地，一个字不动。
            // 与结局判定共用同一道闸(GroupAvatarTurn.FindEnd,即 Classify 判 Ended 的口径):历史里有成立的
            // 结束调用时离席就真结束——哪怕这一轮失败或被停,告别话也照样进群;只发第一次调用的 say,
            // 与回执的 reason/summary 同口径。无限模式下调用被拦,FindEnd 返回 null,不进群
            void PostEndCallSays()
            {
                if (GroupAvatarTurn.FindEnd(avatar.History.Skip(start), endCallsBlocked) is not { } end) return;
                if (string.IsNullOrWhiteSpace(end.Say)) return;
                PostBody(end.Say, DateTimeOffset.UtcNow);
            }

            using GroupMemberReplyFeed replies = new(avatar, PostFromAvatar);
            ChatMessage deliveryMessage = GroupTranscript.DeliveryMessage(input, []);
            bool completed = await RunTurnAsync(avatar, deliveryMessage, null, gate.Token);
            replies.Finish(completed);
            PostEndCallSays();
            // 装配阶段就被停下：投递没进它的历史，游标退回去下次照常交
            if (!completed && !avatar.History.Any(x => ReferenceEquals(x, deliveryMessage)))
            {
                lock (_locker) avatar.GroupCursor = cursor;
            }

            avatar.SaveMeta(touchUpdatedAt: false);

            bool preempted = gate.WasPreempted(completed);
            NoteAvatarTurn(group.SessionId, preempted);
            return GroupAvatarTurn.Classify(avatar.History.Skip(start).ToList(), completed,
                turn.IsCancellationRequested && !cancellationToken.IsCancellationRequested, posted, endCallsBlocked,
                preempted);
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
        lock (_locker) _avatarTurns.GetValueOrDefault(groupId)?.Cancel();
    }
}
