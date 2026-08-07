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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Scheduler;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Utils.Tools;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 思考过程条目(默认折叠,弱化展示)
/// </summary>
public partial class ThinkingItem : ConversationItemBase
{
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// UI 侧节流。思考流是全场最高频、最低价值的一路,而每次把累积全文重设给
    /// TextBlock 都要一次全量文本重排,成本随长度二次增长——本地模型下子代理思考几千 token
    /// 就足以把界面拖住。节流到 ~7Hz 仍然"看得见它在想",重排次数却降两个数量级。
    /// 尾巴由 <see cref="Flush"/> 保证不丢。
    /// </summary>
    private readonly ValueUiDelayUpdater<object?> _throttle;

    [ObservableProperty] private bool _isExpanded;

    public ThinkingItem()
    {
        // 传 null 而非全文:值在真正触发时才从 buffer 取,免得每个 token 都白白拼一次全文
        _throttle = new ValueUiDelayUpdater<object?>(_ => Message = _buffer.ToString(), 150);
    }

    /// <summary>
    /// 追加一段流式增量
    /// </summary>
    /// <param name="delta">增量文本</param>
    public void Append(string delta)
    {
        _buffer.Append(delta);
        _ = _throttle.UpdateValue(null);
    }

    /// <summary>
    /// 立即把缓冲同步到 <see cref="ConversationItemBase.Message"/>(段落收尾时调用)
    /// </summary>
    public void Flush()
    {
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

    /// <summary>
    /// 本次调用<b>内部</b>的过程条目(目前只有子代理会产生)。空集合即普通工具,卡片上不出现入口。
    ///
    /// 不在卡片里就地渲染:条目模板是按整幅宽度设计的,嵌进卡片会被层层削宽、
    /// 滚动区互相嵌套。改由 <see cref="SubAgentActivityWindow"/> 只读展开。
    /// 只存内存,不落盘:过程的价值集中在刚跑完那几分钟,而它一旦进会话档就有回灌进模型上下文的风险。
    /// </summary>
    public ObservableCollection<ConversationItemBase> NestedItems { get; } = new();

    [ObservableProperty] private string _argumentSummary = string.Empty;
    [ObservableProperty] private string _argumentsJson = string.Empty;
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private bool _isSuccess = true;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _isExpanded;

    [RelayCommand]
    private void ShowActivity()
    {
        SubAgentActivityWindow.Show(this);
    }
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

    /// <summary>shell 审批时按命令派生的"同类命令"放行模式;非 shell 或取不到命令为空串</summary>
    public string SuggestedCommandPattern { get; }

    /// <summary>编辑类工具(Write/Replace/Edit)的变更 diff;其余工具为空</summary>
    public IReadOnlyList<DiffLineView> DiffLines { get; } = [];

    /// <summary>是否有 diff 可展示(编码场景的审批体验就是 diff 体验)</summary>
    public bool HasDiff => DiffLines.Count > 0;

    /// <summary>是否提供"记住同类命令"选项(工具级的"本会话总是允许"对 shell 过粗)</summary>
    public bool CanRememberCommandPattern => SuggestedCommandPattern.Length > 0;

    /// <summary>用户点"记住同类命令"时回调(由会话侧写入并持久化)</summary>
    public Action<string>? RememberShellPatternCallback { get; set; }

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
            SuggestedCommandPattern = call.Name == CharacterRunnerFactory.ShellToolName
                ? ApprovalModeMapper.DeriveCommandPattern(
                    ApprovalModeMapper.ExtractCommand(call.Arguments) ?? string.Empty)
                : string.Empty;
            DiffLines = DiffLineView.BuildForToolCall(call);
        }
        else
        {
            ToolName = request.ToolCall?.ToString() ?? "unknown";
            ArgumentSummary = string.Empty;
            SuggestedCommandPattern = string.Empty;
        }
    }

    /// <summary>
    /// 审批动作:once / session / session-command / deny
    /// </summary>
    /// <param name="decision">决定代号</param>
    [RelayCommand]
    private void Resolve(string decision)
    {
        if (IsResolved) return;

        // "记住同类命令":先入会话放行清单(后续同类命令由审批规则直接放行),本次按普通允许回应
        if (decision == "session-command" && CanRememberCommandPattern)
        {
            RememberShellPatternCallback?.Invoke(SuggestedCommandPattern);
        }

        AIContent response = decision switch
        {
            "session" => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.AlwaysInSession,
                "User chose to always approve this tool"),
            "deny" => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Deny, "User denied"),
            _ => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Once, "User approved"),
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
            new[] { ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Deny, "Run stopped") }));
    }
}

/// <summary>
/// 错误条目
/// </summary>
public partial class ErrorItem : ConversationItemBase
{
}

/// <summary>
/// 交接文档条目。压缩是会丢信息的操作，悄悄发生比丢信息本身更糟——
/// 显示出来，既能让人知道「模型从这里往前看不见了」，也能当场发现文档写砸了。
/// 默认折叠：它通常很长，展开是查证用的，不该挤占正常对话。
/// </summary>
public partial class HandoffItem : ConversationItemBase
{
    /// <summary>是否展开</summary>
    [ObservableProperty] private bool _isExpanded;
}

/// <summary>
/// 编辑审批卡片的一条 diff 行。编码场景的审批体验就是 diff 体验——
/// 裸拼的 old/new 参数文本看不出改了什么,等于逼人盲批。
/// </summary>
public sealed class DiffLineView
{
    private const int MaxDisplayLines = 300; //展示上限,超出折叠为提示行

    /// <summary>行前缀(+/-/空格)</summary>
    public string Prefix { get; private init; } = " ";

    /// <summary>行内容</summary>
    public string Text { get; private init; } = string.Empty;

    /// <summary>是否新增行</summary>
    public bool IsAdded { get; private init; }

    /// <summary>是否删除行</summary>
    public bool IsRemoved { get; private init; }

    /// <summary>
    /// 从编辑类工具调用构建 diff 行;非编辑类工具返回空
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>diff 行列表</returns>
    public static IReadOnlyList<DiffLineView> BuildForToolCall(FunctionCallContent call)
    {
        try
        {
            List<DiffLineView> lines = call.Name switch
            {
                "Replace" => BuildReplaceDiff(call.Arguments),
                "Write" => BuildWriteDiff(call.Arguments),
                "Edit" => BuildLineEditsDiff(call.Arguments),
                _ => [],
            };
            return Cap(lines);
        }
        catch
        {
            return []; //diff 只是展示增强,构建失败回退为原始参数摘要
        }
    }

    private static List<DiffLineView> BuildReplaceDiff(IDictionary<string, object?>? args)
    {
        string? oldText = GetString(args, "oldString");
        string? newText = GetString(args, "newString");
        if (oldText == null || newText == null) return [];

        List<DiffLineView> lines = WithHeader(args);
        foreach (LineDiffEntry entry in LineDiff.Compute(oldText, newText))
        {
            lines.Add(entry.Kind switch
            {
                ELineDiffKind.Added => Added(entry.Text),
                ELineDiffKind.Removed => Removed(entry.Text),
                _ => Context(entry.Text),
            });
        }

        return lines;
    }

    private static List<DiffLineView> BuildWriteDiff(IDictionary<string, object?>? args)
    {
        string? content = GetString(args, "content");
        if (content == null) return [];

        List<DiffLineView> lines = WithHeader(args);
        foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
        {
            lines.Add(Added(line));
        }

        return lines;
    }

    private static List<DiffLineView> BuildLineEditsDiff(IDictionary<string, object?>? args)
    {
        if (args?.TryGetValue("lineEdits", out object? value) != true) return [];
        if (value is not JsonElement { ValueKind: JsonValueKind.Array } array) return [];

        List<DiffLineView> lines = WithHeader(args);
        foreach (JsonElement edit in array.EnumerateArray())
        {
            int lineNumber = GetInt(edit, "line_number") ?? GetInt(edit, "lineNumber") ?? 0;
            string newLine = (GetJsonString(edit, "new_line") ?? GetJsonString(edit, "newLine") ?? string.Empty)
                .TrimEnd('\n');
            lines.Add(newLine.Length == 0
                ? Removed($"@{lineNumber}: (delete line)")
                : Added($"@{lineNumber}: {newLine}"));
        }

        return lines;
    }

    private static List<DiffLineView> WithHeader(IDictionary<string, object?>? args)
    {
        string? path = GetString(args, "filePath");
        return string.IsNullOrEmpty(path) ? [] : [Context($"@ {path}")];
    }

    private static IReadOnlyList<DiffLineView> Cap(List<DiffLineView> lines)
    {
        if (lines.Count <= MaxDisplayLines) return lines;
        int omitted = lines.Count - MaxDisplayLines;
        lines.RemoveRange(MaxDisplayLines, omitted);
        lines.Add(Context($"…(+{omitted} more lines)"));
        return lines;
    }

    private static DiffLineView Added(string text) => new() { Prefix = "+", Text = text, IsAdded = true };
    private static DiffLineView Removed(string text) => new() { Prefix = "-", Text = text, IsRemoved = true };
    private static DiffLineView Context(string text) => new() { Prefix = " ", Text = text };

    private static string? GetString(IDictionary<string, object?>? args, string name)
    {
        if (args == null || !args.TryGetValue(name, out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }

    private static string? GetJsonString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }
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
        if (toolName == VisionTool.ToolName) return "👁";
        if (toolName == SchedulerTools.ToolName) return "⏰";
        if (toolName == SubAgentTool.ToolName) return "🤖";
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
        foreach (string key in new[]
                 { "command", "path", "pattern", "skillName", "displayName", "imagePath", "task" })
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
