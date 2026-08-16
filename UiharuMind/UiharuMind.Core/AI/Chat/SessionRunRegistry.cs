/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Chat;

/// <summary>会话运行态</summary>
public enum ESessionRunState
{
    /// <summary>没有轮次在跑</summary>
    Idle,

    /// <summary>有轮次在跑</summary>
    Running,

    /// <summary>有轮次卡在工具审批上等人回应</summary>
    AwaitingApproval,
}

/// <summary>
/// 会话运行态登记处：谁在跑、谁在等审批。
///
/// 记在这一层而不是会话本体或执行者上，因为取用方只有会话标识：界面列表拿到的是
/// <see cref="ChatSessionMeta"/> 投影，本体可能还没加载、也可能已被 <c>Release</c> 放掉，
/// 执行者更会被 <c>DisposeRunnerAsync</c> 抛掉后惰性重建，状态挂在它们身上都会随之消失。
///
/// <b>用引用计数而不是布尔</b>：同一个会话可能有多个消费方先后发起轮次——界面那一轮与
/// 定时任务的无头那一轮就会在执行者的闸门上排队。布尔会被先结束的那个抹掉，
/// 于是指示器提前熄灭、「跑时禁用删除」提前解禁，而另一轮还在写这个会话的文件。
/// </summary>
public sealed class SessionRunRegistry
{
    private readonly object _locker = new();
    private readonly Dictionary<string, int> _runs = new(); //会话 → 在跑的轮次数
    private readonly Dictionary<string, int> _approvals = new(); //会话 → 卡在审批上的轮次数

    /// <summary>
    /// 某个会话的运行态变了。<b>可能来自后台线程</b>（无头执行不在 UI 线程上），
    /// 界面订阅方自行 marshal
    /// </summary>
    public event Action<string>? StateChanged;

    /// <summary>
    /// 取会话的运行态
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>运行态；标识为空视为空闲</returns>
    public ESessionRunState StateOf(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return ESessionRunState.Idle;
        lock (_locker)
        {
            if (_approvals.ContainsKey(sessionId)) return ESessionRunState.AwaitingApproval;
            return _runs.ContainsKey(sessionId) ? ESessionRunState.Running : ESessionRunState.Idle;
        }
    }

    /// <summary>
    /// 会话是否有轮次未结束（含卡在审批上的）。删除、清空历史这类会动文件的操作据此拦下
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>是否忙</returns>
    public bool IsBusy(string? sessionId) => StateOf(sessionId) != ESessionRunState.Idle;

    /// <summary>
    /// 当前所有非空闲会话的快照。导航栏的角标要按「哪一页有会话在忙」聚合，
    /// 而分页归属只有取用方（按会话档位）分得清，所以这里只给原始清单
    /// </summary>
    /// <returns>会话标识与其状态</returns>
    public List<KeyValuePair<string, ESessionRunState>> ActiveSessions()
    {
        lock (_locker)
        {
            List<KeyValuePair<string, ESessionRunState>> active = new(_runs.Count + _approvals.Count);
            foreach (string sessionId in _runs.Keys)
            {
                active.Add(new(sessionId, _approvals.ContainsKey(sessionId)
                    ? ESessionRunState.AwaitingApproval
                    : ESessionRunState.Running));
            }

            // 理论上审批一定嵌在某一轮里面,但别让这个假设决定清单的完整性
            foreach (string sessionId in _approvals.Keys)
            {
                if (!_runs.ContainsKey(sessionId))
                    active.Add(new(sessionId, ESessionRunState.AwaitingApproval));
            }

            return active;
        }
    }

    /// <summary>
    /// 标记一轮开始，<see cref="IDisposable.Dispose"/> 时标记结束。
    /// 必须放在 <c>using</c> 或 try/finally 里——漏一次这个会话就永久停在「在跑」上
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>结束标记用的作用域</returns>
    public IDisposable BeginRun(string? sessionId) => new Scope(this, sessionId, _runs);

    /// <summary>
    /// 标记一轮开始等待工具审批，<see cref="IDisposable.Dispose"/> 时结束等待。
    /// 嵌在 <see cref="BeginRun"/> 的作用域内部使用
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>结束等待用的作用域</returns>
    public IDisposable BeginApprovalWait(string? sessionId) => new Scope(this, sessionId, _approvals);

    private void Increase(string sessionId, Dictionary<string, int> counters)
    {
        lock (_locker)
        {
            counters[sessionId] = counters.GetValueOrDefault(sessionId) + 1;
        }

        StateChanged?.Invoke(sessionId);
    }

    private void Decrease(string sessionId, Dictionary<string, int> counters)
    {
        lock (_locker)
        {
            int count = counters.GetValueOrDefault(sessionId) - 1;
            if (count > 0) counters[sessionId] = count;
            else counters.Remove(sessionId);
        }

        StateChanged?.Invoke(sessionId);
    }

    /// <summary>计数作用域。重复 Dispose 只减一次(using 嵌 return 时容易发生)</summary>
    private sealed class Scope : IDisposable
    {
        private readonly SessionRunRegistry _owner;
        private readonly string? _sessionId;
        private readonly Dictionary<string, int> _counters;
        private bool _released;

        public Scope(SessionRunRegistry owner, string? sessionId, Dictionary<string, int> counters)
        {
            _owner = owner;
            _sessionId = sessionId;
            _counters = counters;
            if (!string.IsNullOrEmpty(sessionId)) owner.Increase(sessionId, counters);
        }

        public void Dispose()
        {
            if (_released || string.IsNullOrEmpty(_sessionId)) return;
            _released = true;
            _owner.Decrease(_sessionId, _counters);
        }
    }
}
