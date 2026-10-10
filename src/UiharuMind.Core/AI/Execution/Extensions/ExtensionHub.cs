/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 扩展的查与调（ADR 0079）：内置扩展就地执行，其余名字交给 MCP 桥接。
/// 一个实例绑定一次装配（这个会话的浏览器标签页、草稿目录、角色禁用名单）。
///
/// <b>所有失败都是返回值，不是异常</b>：没有这个扩展、缺前提、参数不对、执行出错，都变成一条工具结果，
/// 模型据此改参数或换路
/// </summary>
internal sealed class ExtensionHub
{
    private readonly IReadOnlyList<BuiltinExtension> _builtins;
    private readonly McpBridge? _mcp;

    /// <summary>
    /// 绑定一次装配
    /// </summary>
    /// <param name="builtins">这次装配可用的内置扩展</param>
    /// <param name="mcp">MCP 桥接；没有可用 server 为 null</param>
    public ExtensionHub(IReadOnlyList<BuiltinExtension> builtins, McpBridge? mcp)
    {
        _builtins = builtins;
        _mcp = mcp;
    }

    /// <summary>
    /// 查：不带扩展名列出全部；带扩展名列它的命令；再带命令名给完整参数
    /// </summary>
    /// <param name="extension">扩展名；空则列出全部</param>
    /// <param name="command">命令名；空则列出该扩展的命令</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本</returns>
    public async Task<string> HelpAsync(string? extension, string? command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(extension)) return ListAll();

        BuiltinExtension? builtin = Find(extension);
        if (builtin == null)
        {
            return Disabled(extension) ?? (_mcp != null
                ? await _mcp.HelpAsync(extension, command, cancellationToken).ConfigureAwait(false)
                : Unknown(extension));
        }

        List<McpToolDescriptor> commands = builtin.Commands.Select(Describe).ToList();
        string? note = builtin.Unavailable is { } reason ? $"Not available right now: {reason}" : null;
        if (string.IsNullOrWhiteSpace(command)) return McpHelpFormatter.RenderServer(builtin.Name, builtin.Guide, commands, note);

        string? name = BuiltinExtensions.CanonicalCommand(builtin.Name, command);
        McpToolDescriptor? found = commands.FirstOrDefault(x => x.Name == name);
        return found != null
            ? McpHelpFormatter.RenderTool(builtin.Name, found, note)
            : UnknownCommand(builtin, command);
    }

    /// <summary>
    /// 调用一条命令
    /// </summary>
    /// <param name="extension">扩展名</param>
    /// <param name="command">命令名</param>
    /// <param name="arguments">参数对象；无参数传 null</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令的结果（字符串，或含图片等的内容列表）</returns>
    public async Task<object?> CallAsync(string? extension, string? command,
        IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(command))
        {
            return MissingTopLevel(string.IsNullOrWhiteSpace(extension) ? "extension" : "command", arguments);
        }

        BuiltinExtension? builtin = Find(extension);
        if (builtin == null)
        {
            return Disabled(extension) ?? (_mcp != null
                ? await _mcp.CallAsync(extension, command, arguments, cancellationToken).ConfigureAwait(false)
                : Unknown(extension));
        }

        if (builtin.Unavailable is { } reason) return $"The {builtin.Name} extension is not available: {reason}";
        // 与审批同一个解析（ExtensionCalls.TryRead）：执行这边若更宽容（比如模糊匹配 hand_off），
        // 审批按「没这条命令」判成一般级，执行却落到接管上——硬问就被绕过了
        string? name = BuiltinExtensions.CanonicalCommand(builtin.Name, command);
        ExtensionCommand? found = builtin.Commands.FirstOrDefault(x => x.Name == name);
        if (found == null) return UnknownCommand(builtin, command);

        string mention = BuiltinExtensions.Mention(builtin.Name, found.Name);
        JsonElement schema = found.Function.JsonSchema;
        string lookUp = $"Parameters: {McpHelpFormatter.Signature(schema)}. ExtensionHelp with extension " +
                        $"\"{builtin.Name}\" and command \"{found.Name}\" describes each one.";
        // 缺必填先在这里说：交给框架绑定的话，缺参数与命令跑起来出的错都是同一类异常，分不出该改参数还是别重试。
        // 多余的参数名一起报：猜成 ref/text 的模型只看「缺 action」会接着猜，看到「ref 不认识」才会去对签名
        HashSet<string> required = McpHelpFormatter.RequiredOf(schema);
        List<string> missing = required.Where(x => arguments?.ContainsKey(x) != true).ToList();
        if (missing.Count > 0)
        {
            IReadOnlyCollection<string>? known = McpHelpFormatter.PropertyNames(schema);
            List<string> unknown = known == null || arguments == null
                ? []
                : arguments.Keys.Where(x => !known.Contains(x)).ToList();
            string extra = unknown.Count > 0 ? $"; unknown {string.Join(", ", unknown)}" : string.Empty;
            return $"Invalid arguments for {mention}: missing {string.Join(", ", missing)}{extra}. {lookUp}";
        }

        AIFunctionArguments callArguments = new(
            arguments?.ToDictionary(x => x.Key, object? (x) => x.Value) ?? new Dictionary<string, object?>());
        try
        {
            return await found.Function.InvokeAsync(callArguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException e)
        {
            // 值的类型不对（字符串给成了对象之类）：照着帮助改一次就好
            return $"Invalid arguments for {mention}: {e.Message} {lookUp}";
        }
        catch (Exception e)
        {
            return $"{mention} failed: {e.Message}";
        }
    }

    // 弱模型常把 command 塞进 arguments 里；只回「command 必填」它会去补里层那个，越改越远
    private static string MissingTopLevel(string field, IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        string misplaced = arguments != null && (arguments.ContainsKey("command") || arguments.ContainsKey("extension"))
            ? " It was put inside `arguments`; `extension` and `command` go at the top level, and `arguments` holds " +
              "only the command's own parameters."
            : string.Empty;
        return $"Parameter `{field}` is required.{misplaced} Example: {BuiltinExtensions.CallExample}";
    }

    // 内置在前、MCP 在后；状态只出现在这里（工具结果），不进系统提示
    private string ListAll()
    {
        StringBuilder sb = new("Extensions (call ExtensionHelp with `extension` for its commands):\n");
        foreach (BuiltinExtension builtin in _builtins)
        {
            sb.Append("- ").Append(builtin.Name).Append(" (built-in");
            if (builtin.Unavailable is { } reason) sb.Append(", not available: ").Append(reason);
            sb.Append(")\n");
        }

        foreach (McpServerBrief server in _mcp?.List() ?? [])
        {
            if (Find(server.Name) != null)
            {
                sb.Append("- ").Append(server.Name)
                    .Append(" (MCP, hidden: its name clashes with a built-in extension; the user can rename it in settings)\n");
                continue;
            }

            sb.Append("- ").Append(server.Name).Append(" (MCP, ").Append(Describe(server.Status)).Append(')');
            if (!string.IsNullOrWhiteSpace(server.Description)) sb.Append(": ").Append(server.Description.Trim());
            sb.Append('\n');
        }

        if (_builtins.Count == 0 && _mcp == null) sb.Append("(none)\n");
        sb.Append("MCP servers are apps the user runs; one that is offline now may come up later, so check again " +
                  "rather than assuming it is gone.");
        return sb.ToString();
    }

    private static string Describe(McpServerStatus status) => status.State switch
    {
        EMcpConnectionState.Connected => $"connected, {status.ToolCount} commands",
        EMcpConnectionState.Connecting => "connecting",
        EMcpConnectionState.Failed => $"offline: {status.Error ?? "connection failed"}",
        _ => "not connected yet; looking it up connects it",
    };

    private BuiltinExtension? Find(string name) => BuiltinExtensions.Canonical(name) is { } canonical
        ? _builtins.FirstOrDefault(x => x.Name == canonical)
        : null;

    // 用命令名渲染：帮助里写的是 snapshot，不是背后那把工具的名字
    private static McpToolDescriptor Describe(ExtensionCommand command) => new()
    {
        Name = command.Name,
        Description = command.Function.Description,
        InputSchema = command.Function.JsonSchema.Clone(),
    };

    // 内置扩展这次没装上（角色禁了，或这个场合没有）：说清楚，别让它去问 MCP 再得到一句「不认识」
    private static string? Disabled(string extension) => BuiltinExtensions.Canonical(extension) is { } name
        ? $"The {name} extension is turned off for this agent."
        : null;

    private static string UnknownCommand(BuiltinExtension builtin, string command) =>
        $"The {builtin.Name} extension has no command '{command}'. " +
        $"Its commands: {string.Join(", ", builtin.Commands.Select(x => $"{x.Name} {McpHelpFormatter.Signature(x.Function.JsonSchema)}"))}.";

    private static string Unknown(string extension) =>
        $"Unknown extension '{extension}'. ExtensionHelp with no arguments lists every extension.";
}
