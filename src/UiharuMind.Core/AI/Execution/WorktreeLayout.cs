/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// worktree 落盘的磁盘布局：<b>项目级共享</b>，与记忆（<see cref="MemoryLayout"/>）同一棵家目录
/// （<c>Data/Agent/Workspaces/{工作区}_{哈希}/Worktrees/</c>）。跨会话共享、跟工作区走——
/// 换绑工作区就换一棵，旧的不搬不删。
///
/// 刻意不落进仓库兄弟目录：那会散在用户磁盘上、换机器还得搬；落数据目录里
/// 与草稿、记忆同进退，且天然在会话工作目录之外——正好是「登记后免审批」的边界。
/// </summary>
public static class WorktreeLayout
{
    /// <summary>家目录下的子目录名</summary>
    public const string FolderName = "Worktrees";

    /// <summary>
    /// 某个工作区的 worktree 根目录
    /// </summary>
    /// <param name="workspacePath">会话绑定的工作目录；未绑定为 null 或空串</param>
    /// <returns>根目录绝对路径；无工作区时为空串</returns>
    public static string GetWorktreesRoot(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)) return string.Empty;
        return Path.Combine(MemoryLayout.RootPath, WorkspaceSegment.From(workspacePath), FolderName);
    }
}
