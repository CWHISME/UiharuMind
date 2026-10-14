/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 一次扩展调用拆开后的样子
/// </summary>
/// <param name="Extension">扩展名（内置扩展已换成规范写法）</param>
/// <param name="Command">命令名（内置命令已换成规范写法）</param>
/// <param name="Arguments">命令的参数；没给为 null</param>
public sealed record ExtensionInvocation(string Extension, string Command,
    IReadOnlyDictionary<string, JsonElement>? Arguments);

/// <summary>
/// 一次 <c>ExCall</c> 的三个顶层字段
/// </summary>
/// <param name="Extension">扩展名；没给为 null</param>
/// <param name="Command">命令名；没给为 null</param>
/// <param name="Arguments">命令的参数；没给或不是对象为 null</param>
/// <param name="Unwrapped">是否从套进 <c>arguments</c> 的写法里拆出来的</param>
public sealed record ExtensionCallParts(string? Extension, string? Command,
    Dictionary<string, JsonElement>? Arguments, bool Unwrapped);

/// <summary>
/// 扩展的两把元工具（ADR 0079）叫什么，以及怎么把一次 <see cref="ExtensionCalls.CallName"/> 拆开：
/// 审批规则按拆开的扩展与命令判风险，界面按拆开的内层工具画卡
/// </summary>
public static class ExtensionCalls
{
    /// <summary>查询工具名。提示词里提到它时一律引用这个常量</summary>
    public const string HelpName = "ExHelp";

    /// <summary>调用工具名</summary>
    public const string CallName = "ExCall";

    /// <summary>会话放行名单里代表「全部扩展」的那一项（输入框设置里勾的那个）</summary>
    public const string AllExtensions = "*";

    /// <summary>
    /// 是不是一次扩展调用
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>是为 true</returns>
    public static bool IsCall(FunctionCallContent? call) => call?.Name == CallName;

    /// <summary>
    /// 拆开一次扩展调用。扩展名与命令名缺一不可；参数对象认得出就带上
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="invocation">拆开的调用</param>
    /// <returns>是扩展调用且扩展名、命令名都在为 true</returns>
    public static bool TryRead(FunctionCallContent? call, [NotNullWhen(true)] out ExtensionInvocation? invocation)
    {
        invocation = null;
        if (!IsCall(call) || call!.Arguments is not { } arguments) return false;
        ExtensionCallParts parts = ReadParts(arguments);
        string? extension = parts.Extension?.Trim();
        string? command = parts.Command?.Trim();
        if (string.IsNullOrEmpty(extension) || string.IsNullOrEmpty(command)) return false;

        // 内置扩展换成规范写法：审批记的名、界面认的名都按它，模型写 Browser 还是 browser 不该有两种结果
        if (BuiltinExtensions.Canonical(extension) is { } builtin)
        {
            extension = builtin;
            command = BuiltinExtensions.CanonicalCommand(builtin, command) ?? command;
        }

        invocation = new ExtensionInvocation(extension, command, parts.Arguments);
        return true;
    }

    /// <summary>
    /// 读一次 <c>ExCall</c>：<c>name</c> 是「扩展名.命令名」全名，按<b>第一个</b>点号切开
    /// （命令名里可以再有点号；server 名里的点号由 <see cref="ModelName"/> 换掉，切分因此是确定的），<c>args</c> 是命令自己的参数。
    /// 宽容两种写法：旧字段名（<c>extension</c>、<c>command</c>、<c>arguments</c>，也认得旧会话的调用），
    /// 以及整个调用又套进了参数对象——后者拆出来照做，内层没有参数对象就把其余字段当参数。
    /// 审批（<see cref="TryRead"/>）与执行共用这一份，两边看到的是同一次调用
    /// </summary>
    /// <param name="arguments">ExCall 的原始参数</param>
    /// <returns>拆开的字段</returns>
    public static ExtensionCallParts ReadParts(IDictionary<string, object?> arguments)
    {
        (string? extension, string? command) = ReadName(arguments);
        Dictionary<string, JsonElement>? inner = ReadObject(arguments, ArgsKey) ?? ReadObject(arguments, "arguments");
        bool complete = extension != null && command != null;
        if (complete || inner == null || !inner.Keys.Any(IsNameKey)) return new ExtensionCallParts(extension, command, inner, false);

        Dictionary<string, object?> nested = inner.ToDictionary(x => x.Key, object? (x) => x.Value);
        (string? nestedExtension, string? nestedCommand) = ReadName(nested);
        Dictionary<string, JsonElement> rest = inner.Where(x => !IsNameKey(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        Dictionary<string, JsonElement>? commandArguments = ReadObject(nested, ArgsKey) ?? ReadObject(nested, "arguments")
            ?? (rest.Any(x => x.Key is not (ArgsKey or "arguments")) ? rest : null);
        return new ExtensionCallParts(nestedExtension ?? extension, nestedCommand ?? command, commandArguments, true);
    }

    /// <summary>
    /// 读 <c>ExHelp</c> 的目标：<c>name</c> 是扩展名或「扩展名.命令名」，都没给就是列出全部
    /// </summary>
    /// <param name="arguments">ExHelp 的原始参数</param>
    /// <returns>扩展名与命令名，没给的为 null</returns>
    public static (string? Extension, string? Command) ReadHelpTarget(IDictionary<string, object?> arguments) =>
        ReadName(arguments);

    /// <summary>参数对象的键名</summary>
    public const string ArgsKey = "args";

    private static bool IsNameKey(string key) => key is "name" or "command" or "extension";

    // name 优先，旧字段名 command / extension 照认
    private static (string? Extension, string? Command) ReadName(IDictionary<string, object?> arguments) =>
        Split(ReadString(arguments, "extension"), ReadString(arguments, "name") ?? ReadString(arguments, "command"));

    /// <summary>
    /// MCP server 名在模型那边的写法：点号换成下划线，全名「扩展名.命令名」才切得开
    /// </summary>
    /// <param name="serverName">配置里的 server 名</param>
    /// <returns>给模型看、由模型写回的名字</returns>
    public static string ModelName(string serverName) => serverName.Replace('.', '_');

    // 有单独的扩展名就照用（命令名带了同名前缀则去掉）；否则全名按第一个点号切
    private static (string? Extension, string? Command) Split(string? extension, string? command)
    {
        extension = string.IsNullOrWhiteSpace(extension) ? null : extension.Trim();
        command = string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        if (extension != null)
        {
            if (command != null && command.StartsWith(extension + ".", StringComparison.OrdinalIgnoreCase))
                command = command[(extension.Length + 1)..];
            return (extension, command);
        }

        if (command == null) return (null, null);
        int dot = command.IndexOf('.');
        if (dot < 0) return (command, null); //只给了扩展名
        string head = command[..dot].Trim();
        string tail = command[(dot + 1)..].Trim();
        return (head.Length > 0 ? head : null, tail.Length > 0 ? tail : null);
    }

    /// <summary>
    /// 把扩展调用换成内层那次调用：内置命令换回原工具名（<c>GenerateImage</c>、<c>BrowserAct</c>），
    /// MCP 写成 <c>server.tool</c>；CallId 不变。界面因此照旧认得生图、配音、浏览器的卡
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>内层调用；不是扩展调用（或拆不开）原样返回</returns>
    public static FunctionCallContent Unwrap(FunctionCallContent call)
    {
        if (!TryRead(call, out ExtensionInvocation? invocation)) return call;
        Dictionary<string, object?> arguments = invocation.Arguments?
            .ToDictionary(x => x.Key, object? (x) => x.Value) ?? new Dictionary<string, object?>();
        return new FunctionCallContent(call.CallId, InnerName(invocation), arguments);
    }

    /// <summary>
    /// 内层调用叫什么（见 <see cref="Unwrap"/>）
    /// </summary>
    /// <param name="invocation">拆开的调用</param>
    /// <returns>内层工具名</returns>
    public static string InnerName(ExtensionInvocation invocation) =>
        BuiltinExtensions.ToolNameOf(invocation.Extension, invocation.Command)
        ?? $"{invocation.Extension}.{invocation.Command}";

    /// <summary>
    /// 读字符串参数。参数值可能是 <see cref="JsonElement"/>，也可能已经是 CLR 字符串，两者都认
    /// </summary>
    /// <param name="arguments">参数</param>
    /// <param name="key">参数名</param>
    /// <returns>值；没有为 null</returns>
    public static string? ReadString(IDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out object? value)) return null;
        return value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// 读对象参数。宽容一种常见的错法：弱一点的模型会把参数对象写成 JSON <b>字符串</b>，
    /// 那也照收——为此整调用失败，模型拿到的只是一句它无从改正的框架异常
    /// </summary>
    /// <param name="arguments">参数</param>
    /// <param name="key">参数名</param>
    /// <returns>对象的各个字段；没有或不是对象为 null</returns>
    internal static Dictionary<string, JsonElement>? ReadObject(IDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out object? value) || value == null) return null;

        JsonElement element;
        switch (value)
        {
            case JsonElement json:
                element = json;
                break;
            case string text when text.Length > 0:
                try
                {
                    using JsonDocument document = JsonDocument.Parse(text);
                    element = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return null;
                }

                break;
            case IDictionary<string, object?> map:
                element = JsonSerializer.SerializeToElement(map, ToolJson.Arguments);
                break;
            default:
                return null;
        }

        // 字符串里套着的对象再解一层(模型双重编码时)
        if (element.ValueKind == JsonValueKind.String)
        {
            return ReadObject(new Dictionary<string, object?> { [key] = element.GetString() }, key);
        }

        if (element.ValueKind != JsonValueKind.Object) return null;
        Dictionary<string, JsonElement> result = new();
        foreach (JsonProperty property in element.EnumerateObject()) result[property.Name] = property.Value.Clone();
        return result;
    }
}
