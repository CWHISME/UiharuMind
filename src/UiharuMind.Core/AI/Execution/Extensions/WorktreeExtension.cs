/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.Worktree;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// worktree 扩展：git worktree 的创建与登记。登记后 worktree 内文件写入免审批，
/// 不改会话的工作目录（相对路径基准不变）、不动草稿与记忆
/// </summary>
public sealed class WorktreeExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly WorktreeExtension Spec = new();

    public override string Name => BuiltinExtensions.Worktree;

    public override string Guide => Summary;

    public override string Summary =>
        "worktree（隔离工作副本）的创建与登记：创建/登记的 worktree 内文件写入免审批，不动工作目录与草稿目录";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.WorktreeCreate, WorktreeTools.CreateToolName),
        (BuiltinExtensions.WorktreeSet, WorktreeTools.SetToolName),
        (BuiltinExtensions.WorktreeSync, WorktreeTools.SyncToolName),
        (BuiltinExtensions.WorktreeReset, WorktreeTools.ResetToolName),
        (BuiltinExtensions.WorktreeRemove, WorktreeTools.RemoveToolName),
        (BuiltinExtensions.WorktreeList, WorktreeTools.ListToolName),
        (BuiltinExtensions.WorktreeStatus, WorktreeTools.StatusToolName),
    ];

    /// <summary>status/list 纯查询、reset 只撤豁免（恢复要审批，无副作用），都不问；
    /// create/set/sync/remove 有真副作用（拉取、建分支、落盘、改工作树、删目录、授予免审批写入），按一般问一次</summary>
    public override EExtensionRisk Classify(FunctionCallContent call)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        // 没有这条命令：执行只会报错，不为它弹卡（与基类同一口径）
        if (CanonicalCommand(invocation.Command) == null) return EExtensionRisk.ReadOnly;
        return invocation.Command is BuiltinExtensions.WorktreeStatus or BuiltinExtensions.WorktreeList
            or BuiltinExtensions.WorktreeReset
            ? EExtensionRisk.ReadOnly
            : EExtensionRisk.Standard;
    }

    /// <summary>只挂能自己动手、有会话可写登记的场合：主代理/子代理/群成员/化身。
    /// 群里有人离席、化身值守时越界写入被化身硬拒——有自己的 worktree 就有地方改了</summary>
    public override bool MountsIn(EAgentToolScope scope) => scope is
        EAgentToolScope.Main or EAgentToolScope.SubAgent or EAgentToolScope.GroupMember or EAgentToolScope.GroupAvatar;

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        if (resources.Session == null)
        {
            WorktreeExtension unavailable = new();
            unavailable.SetUnavailable("当前没有可用的会话，无法读写 worktree 登记。");
            return unavailable;
        }

        WorktreeExtension assembled = new();
        assembled.SetCommands(Bind(WorktreeTools.Create(resources.Session)));
        return assembled;
    }
}