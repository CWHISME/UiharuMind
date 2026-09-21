/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 工具审批的用户决定
/// </summary>
public enum EApprovalDecision
{
    /// <summary>仅本次允许</summary>
    Once,

    /// <summary>本会话内总是允许该工具</summary>
    AlwaysInSession,

    /// <summary>拒绝</summary>
    Deny,
}

/// <summary>
/// 构造工具审批的回应内容。
/// "总是允许" 由 Microsoft.Agents.AI 的扩展方法提供(preview 面),因此收在 Core 内,
/// 避免 UI 层为了一个方法而直接引用框架。
/// </summary>
public static class ToolApprovalResponseFactory
{
    /// <summary>
    /// 构造审批回应
    /// </summary>
    /// <param name="request">审批请求</param>
    /// <param name="decision">用户决定</param>
    /// <param name="reason">决定理由(会送给模型)</param>
    /// <returns>回应内容</returns>
    public static AIContent Create(ToolApprovalRequestContent request, EApprovalDecision decision, string reason)
    {
        return decision switch
        {
            EApprovalDecision.AlwaysInSession => request.CreateAlwaysApproveToolResponse(reason),
            EApprovalDecision.Deny => request.CreateResponse(approved: false, reason: reason),
            _ => request.CreateResponse(approved: true, reason: reason),
        };
    }
}
