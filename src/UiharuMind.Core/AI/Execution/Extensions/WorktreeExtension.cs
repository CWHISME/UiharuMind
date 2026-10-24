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
using UiharuMind.Core.AI.Execution.Tools.Desktop;
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
        // (BuiltinExtensions.WorktreeReset, WorktreeTools.ResetToolName), // 已临时注释（2026-10-24）：见 WorktreeTools.Create
        (BuiltinExtensions.WorktreeRemove, WorktreeTools.RemoveToolName),
        (BuiltinExtensions.WorktreeList, WorktreeTools.ListToolName),
        (BuiltinExtensions.WorktreeStatus, WorktreeTools.StatusToolName),
    ];

    /// <summary>status/list/reset 纯查询/只撤豁免（恢复要审批，无副作用），都不问；
    /// create 跟随全自动档免审批（隔离副本不影响主工作区；其余档按一般问一次）；
    /// remove/set 要求额外审批（逐项：删目录、登记任意已有目录为免审批，create 的放行不连带）</summary>
    public override EExtensionRisk Classify(FunctionCallContent call)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        // 没有这条命令：执行只会报错，不为它弹卡（与基类同一口径）
        if (CanonicalCommand(invocation.Command) == null) return EExtensionRisk.ReadOnly;
        return invocation.Command switch
        {
            BuiltinExtensions.WorktreeStatus or BuiltinExtensions.WorktreeList =>
                EExtensionRisk.ReadOnly,
            // reset 已临时注释（2026-10-24）：命令不存在自然走下面的未知命令分支 ReadOnly
            BuiltinExtensions.WorktreeCreate => EExtensionRisk.FullAutoOnly,
            BuiltinExtensions.WorktreeRemove or BuiltinExtensions.WorktreeSet => EExtensionRisk.PerScope,
            _ => EExtensionRisk.Standard,
        };
    }

    /// <summary>「本会话允许」按命令记格：remove/set 单独一格，create 放行不连带它们；其余整个 worktree 记</summary>
    public override string ApprovalKey(ExtensionInvocation invocation, FunctionCallContent call,
        DesktopHost? desktop = null)
    {
        return invocation.Command switch
        {
            BuiltinExtensions.WorktreeRemove => $"{Name}:remove",
            BuiltinExtensions.WorktreeSet => $"{Name}:set",
            _ => Name,
        };
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