/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.ViewModels.Conversation;

namespace UiharuMind.ViewModels.Agent;

/// <summary>
/// 思考过程条目(默认折叠,弱化展示)
/// </summary>
public partial class ThinkingItem : ConversationItemBase
{
    private readonly StringBuilder _buffer = new();

    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// 追加一段流式增量
    /// </summary>
    /// <param name="delta">增量文本</param>
    public void Append(string delta)
    {
        _buffer.Append(delta);
        Message = _buffer.ToString();
    }
}

/// <summary>
/// 工具调用卡片:按工具类别渲染(shell / 文件 / 技能 / MCP / 通用)
/// </summary>
public partial class ToolCallItem : ConversationItemBase
{
    public string CallId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;

    /// <summary>卡片图标(按工具类别)</summary>
    public string IconGlyph { get; init; } = "🔧";

    [ObservableProperty] private string _argumentSummary = string.Empty;
    [ObservableProperty] private string _argumentsJson = string.Empty;
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private bool _isSuccess = true;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _isExpanded;
}

/// <summary>
/// 审批卡片:三态回应,决定以 ChatMessage 形式回传给运行循环
/// </summary>
public partial class ApprovalRequestItem : ConversationItemBase
{
    private readonly ToolApprovalRequestContent _request;
    private readonly TaskCompletionSource<ChatMessage> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>待审批工具名</summary>
    public string ToolName { get; }

    /// <summary>参数摘要(审批展示)</summary>
    public string ArgumentSummary { get; }

    [ObservableProperty] private bool _isResolved;
    [ObservableProperty] private string _resolvedText = string.Empty;

    /// <summary>用户决定完成后的回应消息</summary>
    public Task<ChatMessage> Response => _completion.Task;

    public ApprovalRequestItem(ToolApprovalRequestContent request)
    {
        _request = request;
        if (request.ToolCall is FunctionCallContent call)
        {
            ToolName = call.Name;
            ArgumentSummary = AgentContentFormatter.SummarizeArguments(call);
        }
        else
        {
            ToolName = request.ToolCall?.ToString() ?? "unknown";
            ArgumentSummary = string.Empty;
        }
    }

    /// <summary>
    /// 审批动作:once / session / deny
    /// </summary>
    /// <param name="decision">决定代号</param>
    [RelayCommand]
    private void Resolve(string decision)
    {
        if (IsResolved) return;
        AIContent response = decision switch
        {
            "session" => _request.CreateAlwaysApproveToolResponse("User chose to always approve this tool"),
            "deny" => _request.CreateResponse(approved: false, reason: "User denied"),
            _ => _request.CreateResponse(approved: true, reason: "User approved"),
        };
        ResolvedText = decision;
        IsResolved = true;
        _completion.TrySetResult(new ChatMessage(ChatRole.User, new[] { response }));
    }

    /// <summary>
    /// 外部取消(停止运行时):按拒绝处理
    /// </summary>
    public void CancelAsDeny()
    {
        if (IsResolved) return;
        IsResolved = true;
        ResolvedText = "deny";
        _completion.TrySetResult(new ChatMessage(ChatRole.User,
            new[] { (AIContent)_request.CreateResponse(approved: false, reason: "Run stopped") }));
    }
}

/// <summary>
/// 错误条目
/// </summary>
public partial class ErrorItem : ConversationItemBase
{
}

/// <summary>
/// AIContent 展示辅助:工具卡片的图标与参数摘要
/// </summary>
public static class AgentContentFormatter
{
    /// <summary>框架内务工具(todo/mode),不进事件流卡片</summary>
    public static bool IsHousekeepingTool(string toolName)
    {
        return toolName.StartsWith("todos_", System.StringComparison.Ordinal) ||
               toolName is "mode_set" or "mode_get";
    }

    /// <summary>
    /// 工具图标
    /// </summary>
    /// <param name="toolName">工具名</param>
    /// <returns>图标字符</returns>
    public static string GetToolIcon(string toolName)
    {
        if (toolName == "run_shell") return "❯";
        if (toolName.StartsWith("file_access_", System.StringComparison.Ordinal)) return "📄";
        if (toolName is "load_skill" or "read_skill_resource" or "run_skill_script") return "✨";
        if (toolName is "ask_vision") return "👁";
        if (toolName is "create_scheduled_task") return "⏰";
        if (toolName.StartsWith("background_agents_", System.StringComparison.Ordinal)) return "🤖";
        return "🔧";
    }

    /// <summary>
    /// 提取参数摘要(命令原文 / 文件路径 / 首个字符串参数)
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>摘要文本</returns>
    public static string SummarizeArguments(FunctionCallContent call)
    {
        if (call.Arguments == null || call.Arguments.Count == 0) return string.Empty;
        foreach (string key in new[] { "command", "path", "pattern", "skillName", "displayName", "imagePath" })
        {
            if (call.Arguments.TryGetValue(key, out object? value) && value != null)
            {
                string text = value.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }

        return string.Join(", ", call.Arguments.Take(2).Select(x => $"{x.Key}: {x.Value}"));
    }
}
