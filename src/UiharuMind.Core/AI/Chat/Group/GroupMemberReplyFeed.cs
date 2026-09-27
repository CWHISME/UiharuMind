using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一位成员一轮里<b>每一次说完</b>都是一条群发言，不只最后那次。
///
/// 插话会把一轮拆成几段：模型答完一句，框架发现注入队列里有别人的新发言，接着再调一次模型——
/// 于是一轮里有好几条「说完了」的回复，各自回应不同的人。只取最后一条的话，前面几条
/// 留在他自己的会话里、群里谁也看不见（实测并行五人群里多数回复就这么丢了）。
///
/// 判据：助手消息有正文、且不带工具调用（带工具调用的是边做边说的旁白，这一步还没说完）。
/// 每次服务调用落盘即扫一遍，说完立刻进群、立刻广播给还在跑的人；一轮收尾再补扫一遍，兜住没走落盘通知的跑法。
/// 每一段各自守 0046 决策 4：这一段里用 SendMessage 发过群，这段正文就只留在他自己那里。
/// 进了群的那条盖上进群标记，他自己的会话里据此挂「已发到群」
/// </summary>
internal sealed class GroupMemberReplyFeed : IDisposable
{
    private readonly ChatSession _group;
    private readonly ChatSession _member;
    private readonly Func<string, Task?> _post;
    private readonly object _historyLock; //群流水的写锁（与 Append 同一把）：别的成员可能正并发往里追加
    private readonly object _sync = new();
    private readonly List<Task> _posted = [];
    private int _scanned; //成员历史里已看过的位置
    private int _segmentStart; //这一段开始时的群流水长度：之后他自己发过群，这段正文就不再贴
    private bool _marked; //这一轮有回复盖了进群标记：它们已经落过盘，收尾要整份重存一次

    /// <summary>
    /// 开始盯一位成员的这一轮
    /// </summary>
    /// <param name="group">群壳</param>
    /// <param name="member">成员会话</param>
    /// <param name="groupCursor">这一轮开始时的群流水长度</param>
    /// <param name="post">把一段正文记成群发言（剥前缀、追加、广播）；没记为 null</param>
    /// <param name="historyLock">群流水的写锁：读群流水时与追加对齐</param>
    public GroupMemberReplyFeed(ChatSession group, ChatSession member, int groupCursor, Func<string, Task?> post,
        object historyLock)
    {
        _group = group;
        _member = member;
        _post = post;
        _historyLock = historyLock;
        _scanned = member.History.Count;
        _segmentStart = groupCursor;
        member.ServiceCallPersisted += OnServiceCallPersisted;
    }

    /// <summary>
    /// 一轮收尾：不再盯落盘通知；正常跑完就补扫一遍
    /// </summary>
    /// <param name="completed">这一轮是否正常跑完；失败或被停时尚未说完的半截不算群发言</param>
    public void Finish(bool completed)
    {
        _member.ServiceCallPersisted -= OnServiceCallPersisted;
        if (completed) Scan();
        // 进群标记盖在已落盘的消息上，不重存的话重开会话就看不出哪几条进了群
        if (_marked) _member.Save();
    }

    /// <summary>
    /// 等这一轮所有进群发言的广播与唤醒做完。放在登记「说完」之后等：
    /// 广播要排队，先等它的话，他在界面上会一直显示在说
    /// </summary>
    /// <returns>都做完</returns>
    public Task WhenPostedAsync()
    {
        lock (_sync) return Task.WhenAll(_posted.ToArray());
    }

    /// <inheritdoc />
    public void Dispose() => _member.ServiceCallPersisted -= OnServiceCallPersisted;

    private void OnServiceCallPersisted() => Scan();

    private void Scan()
    {
        lock (_sync)
        {
            IList<ChatMessage> history = _member.History;
            for (; _scanned < history.Count; _scanned++)
            {
                if (!IsFinishedReply(history[_scanned], out string text)) continue;

                bool sentViaTool;
                lock (_historyLock) sentViaTool = GroupTranscript.PostedSince(_group.History, _segmentStart, _member.SessionId);
                // 回「[跳过]」就是这次不接话：不进群，也就不广播、不叫醒谁
                bool pass = GroupTranscript.IsPass(GroupTranscript.StripSpeakerPrefix(text, _member.CharacterData.CharacterName));
                if (!sentViaTool && !pass && _post(text) is { } posted)
                {
                    _posted.Add(posted);
                    ChatMessageAnnotations.MarkPostedToGroup(history[_scanned]);
                    _marked = true;
                }
                // 必须在 _post 之后取：下一段从他刚贴的这条之后算起，否则下一段会把这条认成「已用工具发过」而吞掉
                lock (_historyLock) _segmentStart = _group.History.Count;
            }
        }
    }

    private static bool IsFinishedReply(ChatMessage message, out string text)
    {
        text = string.Empty;
        if (message.Role != ChatRole.Assistant) return false;
        if (message.Contents.Any(x => x is FunctionCallContent)) return false;

        text = message.Text.Trim();
        return text.Length > 0;
    }
}
