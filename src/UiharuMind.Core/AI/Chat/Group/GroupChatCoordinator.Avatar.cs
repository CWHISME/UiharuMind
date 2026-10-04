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
    /// <returns>这一轮的结局</returns>
    public async Task<GroupAvatarTurn> RunAvatarAsync(ChatSession group, ChatSession avatar, string? note,
        CancellationToken cancellationToken, bool endCallsBlocked = false)
    {
        // 用户正在私聊化身：这次不跑，游标不动
        using IDisposable? gate = GroupMemberTurnGate.TryEnter(avatar.SessionId);
        if (gate == null) return GroupAvatarTurn.Busy;

        using CancellationTokenSource turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string? delivery;
        lock (_locker)
        {
            if (!_avatarTurns.TryAdd(group.SessionId, turn)) return GroupAvatarTurn.Busy;
            delivery = GroupTranscript.BuildDelivery(group.History, avatar.GroupCursor, avatar.SessionId);
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
                if (turn.IsCancellationRequested) return null;
                if (!endCallsBlocked && GroupAvatarTurn.FindEnd(avatar.History.Skip(start)) != null) return null;
                string body = GroupTranscript.StripSpeakerPrefix(text.Trim(), GroupSceneSource.SpeakerNameOf(avatar));
                if (string.IsNullOrWhiteSpace(body)) return null;

                ChatMessage post = group.CreateMessage(ChatRole.User, body, imageContents: null, createdAt);
                ChatMessageAnnotations.MarkGroupAvatarPost(post, avatar.SessionId);
                Interlocked.Increment(ref posted);
                // 不等：这句开的那一波跑完要好一阵，波末会再叫醒化身
                PostUserMessageAsync(group, post).LogOnFault("post for the group avatar");
                return Task.CompletedTask;
            }

            using GroupMemberReplyFeed replies = new(avatar, PostFromAvatar);
            bool completed = await RunTurnAsync(avatar, GroupTranscript.DeliveryMessage(input, []), null, turn.Token);
            replies.Finish(completed);
            avatar.SaveMeta(touchUpdatedAt: false);

            return GroupAvatarTurn.Classify(avatar.History.Skip(start).ToList(), completed,
                turn.IsCancellationRequested && !cancellationToken.IsCancellationRequested, posted,
                endCallsBlocked);
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
