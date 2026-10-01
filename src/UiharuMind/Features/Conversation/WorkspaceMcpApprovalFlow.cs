/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.SidePanels;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 选定工作区时，就地为该项目的 <c>.mcp.json</c> 要一次安全确认。
///
/// <b>时机定在这一刻而不是首轮发送时</b>，理由是它同时解决三件事：
/// 用户当场知道这个项目会连上什么、确认不会在发送后突然弹出来打断，
/// 而最要紧的是——这一刻<b>早于任何子进程启动</b>。
///
/// 弹窗由 App 层主动发起（Core 只提供"查待确认 / 记授权"两个被动 API）：
/// 抛全局事件的做法这个仓库已经踩过，预连提示曾因此点亮到一个跟 MCP 毫无关系的会话上。
///
/// 确认是「全部允许」这一档，但<b>记录仍逐条落</b>（每条各记自己的可执行面指纹）——
/// 于是下次仓库新增第四个 server 时，弹窗只说新增的那一条，而不是把四条重新摆一遍。
/// 后者会养出"看第三次就直接点确认"的习惯，而确认疲劳就是这类机制实际失效的方式。
/// </summary>
public sealed class WorkspaceMcpApprovalFlow
{
    private readonly IMessageService _messages;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="messages">弹确认用的消息服务</param>
    public WorkspaceMcpApprovalFlow(IMessageService messages)
    {
        _messages = messages;
    }

    /// <summary>
    /// 该工作区有待确认的项目级 server 时弹一次确认，批了就记授权
    /// </summary>
    /// <param name="workspacePath">刚选定的工作区；空表示解绑，无事可做</param>
    public async Task PromptAsync(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return;

        try
        {
            List<McpApprovalRequest> pending = McpManager.Instance.GetPendingApprovals(workspacePath);
            if (pending.Count == 0) return;

            if (!await _messages.ConfirmAsync(BuildMessage(workspacePath, pending),
                    Loc.Text(LangKey.AgentMcpApprovalTitle)))
            {
                // 拒绝不落任何记录:下次再进这个工作区会再问一次。
                // 记一条"拒绝过"看着更省事,但那会让"我当时点错了"没有回头路,
                // 而这一问的成本只是一个弹窗
                return;
            }

            McpManager.Instance.ApproveWorkspaceServers(workspacePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Prompt workspace MCP approval failed: {e.Message}");
        }
    }

    // 命令必须逐字摆出来——用户批的是这条命令,不是这个名字
    private static string BuildMessage(string workspacePath, List<McpApprovalRequest> pending)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string changedMark = loc.GetString("AgentMcpApprovalChangedMark");
        StringBuilder list = new();
        foreach (McpApprovalRequest request in pending)
        {
            list.Append("• ").Append(request.Name).Append(":  ").Append(request.CommandLine);
            if (request.IsChanged) list.Append(changedMark);
            list.Append('\n');
        }

        return string.Format(loc.GetString("AgentMcpApprovalBody"),
            WorkspaceDisplay.NameOf(workspacePath), pending.Count, list.ToString());
    }
}
