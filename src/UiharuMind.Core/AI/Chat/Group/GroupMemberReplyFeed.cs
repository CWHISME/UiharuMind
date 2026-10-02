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
/// 进了群的那条盖上进群标记，他自己的会话里据此挂「已发到群」
///
/// <b>不再按「本轮发过群就不贴正文」收口</b>：一条正文不带工具调用的消息按定义不可能自己带
/// <c>SendMessage</c>，所以跨消息去判「同一句话重复贴」永远判不中同义，只判得中「这一轮前面发过群」——
/// 于是一轮里只要中途发过一次群，本轮第一个（常常也是唯一一个）说完必被吞掉，而那通常正是结论
/// （实测：智能体成员发了两次群、中间跑完四分钟压测，末尾那条判决书群里一个字没见着）。
/// 「不重复贴」由结构本身保证：带 <c>SendMessage</c> 的那条永远不算「说完」，永远不贴。
/// </summary>
internal sealed class GroupMemberReplyFeed : IDisposable
{
    private readonly ChatSession _member;
    private readonly Func<string, DateTimeOffset?, Task?> _post;
    private readonly object _sync = new();
    private readonly List<Task> _posted = [];
    private int _scanned; //成员历史里已看过的位置
    private bool _marked; //这一轮有回复盖了进群标记：它们已经落过盘，收尾要整份重存一次

    /// <summary>
    /// 开始盯一位成员的这一轮
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="post">把一段正文记成群发言（剥前缀、追加、广播）；没记为 null。
    /// 第二个参数是成员那条消息的时间，群里沿用它——同一句话两边是同一时刻</param>
    public GroupMemberReplyFeed(ChatSession member, Func<string, DateTimeOffset?, Task?> post)
    {
        _member = member;
        _post = post;
        _scanned = member.History.Count;
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

                // 回「[跳过]」就是这次不接话：不进群，也就不广播、不叫醒谁
                string own = GroupTranscript.StripSpeakerPrefix(text, GroupSceneSource.SpeakerNameOf(_member));
                if (GroupTranscript.IsPass(own)) continue;
                if (_post(text, history[_scanned].CreatedAt) is not { } posted) continue;

                _posted.Add(posted);
                ChatMessageAnnotations.MarkPostedToGroup(history[_scanned]);
                _marked = true;
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
