namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一波由什么开的头：用户的一句发言（给出它在群流水里的下标），或用户点了「继续」
/// </summary>
/// <param name="UserPostIndex">用户那句的下标；「继续」为 null</param>
internal readonly record struct GroupKickoff(int? UserPostIndex);

/// <summary>一条刚追加进群流水的发言</summary>
/// <param name="Index">群流水下标</param>
/// <param name="AuthorSessionId">发言的成员会话；用户为 null</param>
/// <param name="Text">正文</param>
internal readonly record struct GroupPostEvent(int Index, string? AuthorSessionId, string Text);

/// <summary>一位成员跑完一轮的结果</summary>
/// <param name="Ran">真的开口跑了（有新话可接、没被私聊占着）</param>
/// <param name="ConsumedInjections">这一轮插进去且被消费了的群流水下标</param>
internal readonly record struct GroupTurnOutcome(bool Ran, IReadOnlySet<int> ConsumedInjections)
{
    /// <summary>没跑</summary>
    public static GroupTurnOutcome Skipped { get; } = new(false, new HashSet<int>());
}

/// <summary>
/// 调度器向调度宿主要的能力：让某位成员跑一轮、问谁还有新话。
/// 投递、插话、群流水写入都在宿主里，调度器只决定「谁、什么时候」——换调度不动投递（ADR 0046 决策 5）
/// </summary>
internal interface IGroupTurnHost
{
    /// <summary>
    /// 让成员跑一轮：投递游标之后的新话，跑完把他的发言写进群流水
    /// </summary>
    /// <param name="run">所在的一波</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="cause">因何叫醒</param>
    /// <returns>这一轮的结果</returns>
    Task<GroupTurnOutcome> RunMemberAsync(GroupRun run, string memberSessionId, EGroupWakeCause cause);

    /// <summary>这位成员有没有还没听过的群发言</summary>
    /// <param name="group">群壳会话</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <returns>有为 true</returns>
    bool HasNewLines(ChatSession group, string memberSessionId);

    /// <summary>群的成员名单（认 @ 用）</summary>
    /// <param name="group">群壳会话</param>
    /// <returns>名单，发言顺序</returns>
    IReadOnlyList<GroupRosterEntry> RosterOf(ChatSession group);
}

/// <summary>
/// 一波的调度策略（ADR 0049：串行与并行并存）。每波一个实例，可以持有这一波的状态
/// </summary>
internal interface IGroupScheduler
{
    /// <summary>跑这一波，直到没人再在跑</summary>
    /// <param name="kickoff">开头</param>
    Task RunAsync(GroupKickoff kickoff);

    /// <summary>
    /// 这一波里有一条新发言进了群流水（已经广播给正在跑的人）。
    /// 由写入方同步调用，实现里不要阻塞
    /// </summary>
    /// <param name="post">新发言</param>
    void OnPosted(GroupPostEvent post);

    /// <summary>
    /// 这条新发言要不要即时插进某位正在跑的成员这一轮。插进去被消费就会多调一次模型、多一句回复，
    /// 所以它也是一种唤醒，得守同一条停止条件（ADR 0049「实现时定下的」第 12 条）。
    /// 在广播线程上调用，实现要线程安全
    /// </summary>
    /// <param name="post">新发言</param>
    /// <param name="runningMemberSessionId">正在跑的那位成员</param>
    /// <returns>插为 true；不插的等他下一轮随投递看到</returns>
    bool ShouldInject(GroupPostEvent post, string runningMemberSessionId);
}
