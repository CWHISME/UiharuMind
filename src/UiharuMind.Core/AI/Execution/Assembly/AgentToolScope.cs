/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 智能体此刻在什么场合跑。能力开关说「它会什么」，场合说「这里用得上什么」：
/// 挂上的工具 = 角色卡 ∩ 派活者（仅子代理）∩ 场合，三者都是交集（场合表见 <see cref="AgentToolScopes"/>）。
/// </summary>
public enum EAgentToolScope
{
    /// <summary>普通会话里的主代理</summary>
    Main,

    /// <summary>群成员：经群发言工具说话；用户看的是群壳，不开口朗读</summary>
    GroupMember,

    /// <summary>离席的化身：能结束离席；不在群名单里、群叫不醒它，不起后台任务</summary>
    GroupAvatar,

    /// <summary>定时任务的一次运行：跑完就收工，不起活得比它久、还要叫醒它的后台任务</summary>
    ScheduledRun,

    /// <summary>委派出去的子代理：拿的是一份任务书</summary>
    SubAgent,

    /// <summary>脑（ADR 0082）：同步、有界、只读的整理助手，只给 Grep/Read</summary>
    Brain,
}

/// <summary>
/// 一个场合的规矩
/// </summary>
/// <param name="Allowed">这里用得上的能力档</param>
/// <param name="BackgroundTasks">能否起后台任务（跑完要叫醒发起者，叫不醒的场合不给）</param>
/// <param name="ReadOnlyFiles">文件工具是否只给读</param>
internal sealed record AgentToolScopeRule(
    IReadOnlySet<EAgentCapability> Allowed, bool BackgroundTasks, bool ReadOnlyFiles = false)
{
    /// <summary>
    /// 这一档在这个场合用不用得上
    /// </summary>
    /// <param name="capability">能力档</param>
    /// <returns>用得上为 true</returns>
    public bool Allows(EAgentCapability capability) => Allowed.Contains(capability);
}

/// <summary>
/// 场合表：哪个场合用得上哪几档能力。<b>挂不挂一个工具，只有这张表和能力开关说话</b>——
/// 主代理、群成员、化身、定时运行、子代理都经 <see cref="AgentToolsetBuilder"/> 装配，不再各写一份工具名单。
/// 从前子代理那份另写，新加的能力（当时的浏览器、配音、转写）都漏了它，而没有任何地方会报错。
/// </summary>
internal static class AgentToolScopes
{
    private static readonly AgentToolScopeRule MainRule = new(new HashSet<EAgentCapability>
    {
        EAgentCapability.FileAccess, EAgentCapability.Shell, EAgentCapability.WebSearch,
        EAgentCapability.VisionTool, EAgentCapability.Extensions, EAgentCapability.Speak,
        EAgentCapability.KnowledgeSearch, EAgentCapability.ScheduledTasks, EAgentCapability.SubAgent,
        EAgentCapability.Skills,
    }, BackgroundTasks: true);

    // 子代理拿的是一份任务书：不再往下委派（递归）、不自己装载技能、不排定时任务、
    // 没有会话挂载的知识库可检索、不开口朗读（用户看的是派活的那个会话）、不起后台任务。
    // 其余与主代理一样——扩展里的浏览器各开各的标签页，配音、转写落进共用的草稿目录
    private static readonly AgentToolScopeRule SubAgentRule = new(new HashSet<EAgentCapability>
    {
        EAgentCapability.FileAccess, EAgentCapability.Shell, EAgentCapability.WebSearch,
        EAgentCapability.VisionTool, EAgentCapability.Extensions,
    }, BackgroundTasks: false);

    private static readonly AgentToolScopeRule GroupMemberRule =
        new(With(MainRule, add: EAgentCapability.GroupPost, remove: EAgentCapability.Speak), BackgroundTasks: true);

    private static readonly AgentToolScopeRule GroupAvatarRule =
        new(With(MainRule, add: EAgentCapability.GroupAway), BackgroundTasks: false);

    private static readonly AgentToolScopeRule ScheduledRunRule = MainRule with { BackgroundTasks = false };

    // 脑拿的是一份整理任务：只给文件检索（Grep/Read，无 Glob——内容向，目录形状不关心），只读不改
    private static readonly AgentToolScopeRule BrainRule =
        new(new HashSet<EAgentCapability> { EAgentCapability.FileAccess }, BackgroundTasks: false, ReadOnlyFiles: true);

    /// <summary>
    /// 取一个场合的规矩
    /// </summary>
    /// <param name="scope">场合</param>
    /// <returns>规矩</returns>
    public static AgentToolScopeRule Of(EAgentToolScope scope) => scope switch
    {
        EAgentToolScope.Main => MainRule,
        EAgentToolScope.GroupMember => GroupMemberRule,
        EAgentToolScope.GroupAvatar => GroupAvatarRule,
        EAgentToolScope.ScheduledRun => ScheduledRunRule,
        EAgentToolScope.SubAgent => SubAgentRule,
        EAgentToolScope.Brain => BrainRule,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    /// <summary>
    /// 按会话身份判主代理这一侧的场合（子会话走子代理装配，由那边自己判）
    /// </summary>
    /// <param name="profile">会话画像</param>
    /// <returns>场合</returns>
    public static EAgentToolScope ScopeOf(AgentBuildProfile profile) =>
        profile.IsGroupAvatar ? EAgentToolScope.GroupAvatar
        : profile.IsGroupMember ? EAgentToolScope.GroupMember
        : profile.IsScheduledRun ? EAgentToolScope.ScheduledRun
        : EAgentToolScope.Main;

    private static HashSet<EAgentCapability> With(AgentToolScopeRule rule, EAgentCapability add,
        EAgentCapability? remove = null)
    {
        HashSet<EAgentCapability> allowed = new(rule.Allowed) { add };
        if (remove is { } removed) allowed.Remove(removed);
        return allowed;
    }
}
