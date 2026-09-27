using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 假的成员一轮：把输入与一句编号的回答写进成员会话，就像真跑过一样。
/// 并行测试会从多个任务里调它，记录一律加锁
/// </summary>
internal sealed class FakeGroupMemberTurnRunner : IGroupMemberTurnRunner
{
    private readonly object _sync = new();
    private readonly Dictionary<string, int> _spoken = new();
    private readonly List<(ChatSession Member, string Input)> _calls = [];
    private readonly List<(ChatSession Member, string Text)> _injected = [];
    private readonly HashSet<string> _replied = []; //这一轮已经说完的成员：之后插进来的，真模型不会再消费
    private readonly List<ChatMessage> _late = []; //说完之后才插进来的，收尾时撤回

    /// <summary>跑过的每一轮（成员、输入）</summary>
    public List<(ChatSession Member, string Input)> Calls
    {
        get
        {
            lock (_sync) return _calls.ToList();
        }
    }

    /// <summary>插进去的每一句（成员、正文）</summary>
    public List<(ChatSession Member, string Text)> Injected
    {
        get
        {
            lock (_sync) return _injected.ToList();
        }
    }

    /// <summary>成员这一轮中途做的事（在写回答之前）</summary>
    public Dictionary<string, Func<Task>> During { get; } = new();

    /// <summary>成员第 n 次（从 1 起）的回答正文；没给就用编号句</summary>
    public Dictionary<string, Func<int, string>> Replies { get; } = new();

    /// <summary>这几位这一轮失败</summary>
    public HashSet<string> Fail { get; } = [];

    /// <summary>打开后本轮不再产生群发言（log 不再增长），用于构造「全员无新话」的静止场景</summary>
    public bool Silent { get; set; }

    /// <summary>打开后插话一律「没被消费」：收尾时全部撤回（成员已在最后一次调用里）</summary>
    public bool InjectionsNeverConsumed { get; set; }

    /// <summary>清掉已记的轮次（插话记录保留）</summary>
    public void ClearCalls()
    {
        lock (_sync) _calls.Clear();
    }

    /// <summary>某位成员跑过几轮</summary>
    /// <param name="member">成员会话</param>
    /// <returns>轮数</returns>
    public int CallsOf(ChatSession member) => Calls.Count(x => x.Member.SessionId == member.SessionId);

    public async Task<bool> RunAsync(ChatSession member, ChatMessage input, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _calls.Add((member, input.Text));
            _replied.Remove(member.SessionId);
            member.History.Add(input);
        }

        if (During.TryGetValue(member.SessionId, out Func<Task>? during)) await during();
        if (Fail.Contains(member.SessionId) || cancellationToken.IsCancellationRequested) return false;
        if (Silent) return true;

        lock (_sync)
        {
            int count = _spoken.GetValueOrDefault(member.SessionId) + 1;
            _spoken[member.SessionId] = count;
            string text = Replies.TryGetValue(member.SessionId, out Func<int, string>? reply)
                ? reply(count)
                : $"{member.CharacterData.CharacterName} 的第 {count} 次发言";
            member.History.Add(new ChatMessage(ChatRole.Assistant, text));
            _replied.Add(member.SessionId);
        }

        return true;
    }

    public Task<bool> TryInjectAsync(ChatSession member, ChatMessage message)
    {
        lock (_sync)
        {
            _injected.Add((member, message.Text));
            if (_replied.Contains(member.SessionId)) _late.Add(message);
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyCollection<ChatMessage>> WithdrawAsync(ChatSession member,
        IReadOnlyCollection<ChatMessage> messages)
    {
        if (InjectionsNeverConsumed) return Task.FromResult(messages);
        lock (_sync)
        {
            IReadOnlyCollection<ChatMessage> late = messages.Where(_late.Contains).ToList();
            return Task.FromResult(late);
        }
    }
}
