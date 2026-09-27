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
/// 每一段各自守 0046 决策 4：这一段里用 SendMessage 发过群，这段正文就只留在他自己那里
/// </summary>
internal sealed class GroupMemberReplyFeed : IDisposable
{
    private readonly ChatSession _group;
    private readonly ChatSession _member;
    private readonly Func<string, Task?> _post;
    private readonly object _sync = new();
    private readonly List<Task> _posted = [];
    private int _scanned; //成员历史里已看过的位置
    private int _segmentStart; //这一段开始时的群流水长度：之后他自己发过群，这段正文就不再贴

    /// <summary>
    /// 开始盯一位成员的这一轮
    /// </summary>
    /// <param name="group">群壳</param>
    /// <param name="member">成员会话</param>
    /// <param name="groupCursor">这一轮开始时的群流水长度</param>
    /// <param name="post">把一段正文记成群发言（剥前缀、追加、广播）；没记为 null</param>
    public GroupMemberReplyFeed(ChatSession group, ChatSession member, int groupCursor, Func<string, Task?> post)
    {
        _group = group;
        _member = member;
        _post = post;
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

                bool sentViaTool = GroupTranscript.PostedSince(_group.History, _segmentStart, _member.SessionId);
                // 回「[跳过]」就是这次不接话：不进群，也就不广播、不叫醒谁
                bool pass = GroupTranscript.IsPass(GroupTranscript.StripSpeakerPrefix(text, _member.CharacterData.CharacterName));
                if (!sentViaTool && !pass && _post(text) is { } posted) _posted.Add(posted);
                _segmentStart = _group.History.Count;
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
