/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 工作区记忆（ADR 0028）的磁盘布局：<b>一个工作区一个记忆目录</b>，落在
/// agent 产物家目录的 <c>Memory/</c> 子目录（<c>Data/Agent/Workspaces/{工作区}_{哈希}/Memory/</c>），
/// 与各会话产出房间平级、跨会话共享。
/// 没绑工作区的会话沿用同一约定：它的家目录就是会话房间
/// （<c>NoWorkspace/{会话id8}/</c>），记忆随会话生灭。
///
/// 记忆不用框架 provider：模型拿普通文件工具读写，这里只负责把路径算出来，
/// 交给提示词段（<see cref="AgentToolPrompts.BuildMemory"/>）与审批规则（<see cref="ApprovalModeMapper"/>）。
/// </summary>
public static class MemoryLayout
{
    /// <summary>记忆目录名（工作区家目录下的子目录）</summary>
    public const string FolderName = "Memory";

    /// <summary>所有记忆目录的根：与 agent 产物同一棵（ADR 0026 的家目录命名）</summary>
    public static string RootPath => AgentOutputLayout.RootPath;

    /// <summary>
    /// 会话的记忆目录绝对路径。
    /// </summary>
    /// <param name="workspacePath">会话绑定的工作目录；未绑定为 null 或空串</param>
    /// <param name="outputFolderName">产出目录名（<see cref="AgentOutputLayout.GetFolderName"/> 的产物）；
    /// 无会话（能力预览）时为空串</param>
    /// <returns>记忆目录绝对路径；无会话时为空串（= 没有记忆）</returns>
    public static string GetMemoryDirectory(string? workspacePath, string outputFolderName)
    {
        // 绑了工作区:记忆住家目录根(与产出房间平级),跨会话共享;
        // 没绑:家就是会话房间,记忆随会话生灭。两种都只认 Memory/ 这一格(免审批范围,见 ADR 0028)
        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            return Path.Combine(RootPath, WorkspaceSegment.From(workspacePath), FolderName);
        }

        if (string.IsNullOrWhiteSpace(outputFolderName)) return string.Empty;
        return Path.Combine(RootPath, outputFolderName, FolderName);
    }
}