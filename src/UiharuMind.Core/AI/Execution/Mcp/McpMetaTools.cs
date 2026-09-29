/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 按需模式的两个<b>元工具</b>：<c>McpHelp</c>（查）与 <c>McpCall</c>（调）。
///
/// 定义<b>恒定</b>——名字、描述、参数 schema 都不随 server 变化，工具集因此不随连接状态增减，
/// 缓存前缀稳定。挂不挂只看配置（有没有按需 server），不看连接（见 ADR 0051）。
///
/// 手写 schema 与 <see cref="AIFunction"/> 子类而不用 <c>AIFunctionFactory</c>：
/// <c>McpCall</c> 的返回可能是含图片的内容列表，工厂会把返回值序列化掉，
/// 而框架自己的 MCP 包装就是把内容块原样交出去的，这里必须同形。
/// </summary>
internal static class McpMetaTools
{
    /// <summary>查询工具名。提示词里提到它时一律引用这个常量</summary>
    public const string HelpName = "McpHelp";

    /// <summary>调用工具名</summary>
    public const string CallName = "McpCall";

    /// <summary>
    /// 创建两个元工具，共用同一个桥接实例。
    /// </summary>
    /// <param name="bridge">绑定了本次装配上下文的桥接</param>
    /// <returns>McpHelp 与 McpCall</returns>
    public static IReadOnlyList<AIFunction> Create(McpBridge bridge) =>
    [
        new McpHelpFunction(bridge),
        new McpCallFunction(bridge),
    ];

    private const string HelpSchema = """
        {
          "type": "object",
          "properties": {
            "server": {
              "type": "string",
              "description": "Name of the MCP server, as listed in the system prompt."
            },
            "tool": {
              "type": "string",
              "description": "Optional. A tool name on that server; when given, returns that tool's full description and parameter schema."
            }
          },
          "required": ["server"]
        }
        """;

    private const string CallSchema = """
        {
          "type": "object",
          "properties": {
            "server": {
              "type": "string",
              "description": "Name of the MCP server, as listed in the system prompt."
            },
            "tool": {
              "type": "string",
              "description": "Name of the tool on that server, exactly as shown by McpHelp."
            },
            "arguments": {
              "type": "object",
              "description": "The tool's arguments as a JSON object matching its parameter schema. Omit for tools without parameters.",
              "additionalProperties": true
            }
          },
          "required": ["server", "tool"]
        }
        """;

    private sealed class McpHelpFunction : MetaToolFunction
    {
        private readonly McpBridge _bridge;

        public McpHelpFunction(McpBridge bridge) : base(HelpName,
            "Look up MCP servers whose tools are not in your tool list. " +
            "With only `server`: returns the server's usage notes and the names of its tools. " +
            "With `server` and `tool`: returns that tool's full description and parameter schema. " +
            "Call this before McpCall when you do not know a tool's exact name or parameters.",
            HelpSchema)
        {
            _bridge = bridge;
        }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return await _bridge.HelpAsync(ReadString(arguments, "server"), ReadString(arguments, "tool"),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class McpCallFunction : MetaToolFunction
    {
        private readonly McpBridge _bridge;

        public McpCallFunction(McpBridge bridge) : base(CallName,
            "Call a tool on an MCP server whose tools are not in your tool list. " +
            "Use McpHelp first to learn the tool's name and parameters. " +
            "Large results are saved to a file and only the head and tail are returned; " +
            "read the saved file for the rest.",
            CallSchema)
        {
            _bridge = bridge;
        }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return await _bridge.CallAsync(ReadString(arguments, "server"), ReadString(arguments, "tool"),
                ReadObject(arguments, "arguments"), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>固定 schema 的 AIFunction 基类：名字、描述、参数定义在构造时一次定死</summary>
    private abstract class MetaToolFunction : AIFunction
    {
        private readonly string _description;
        private readonly JsonElement _schema;

        protected MetaToolFunction(string name, string description, string schemaJson)
        {
            Name = name;
            _description = description;
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            _schema = document.RootElement.Clone();
        }

        public override string Name { get; }
        public override string Description => _description;
        public override JsonElement JsonSchema => _schema;
    }

    /// <summary>
    /// 读字符串参数。参数值可能是 <see cref="JsonElement"/>，也可能已经是 CLR 字符串，两者都认。
    /// </summary>
    internal static string? ReadString(AIFunctionArguments arguments, string key)
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
    /// 那也照收——为此整调用失败，模型拿到的只是一句它无从改正的框架异常。
    /// </summary>
    internal static Dictionary<string, JsonElement>? ReadObject(AIFunctionArguments arguments, string key)
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
                    using (JsonDocument document = JsonDocument.Parse(text))
                    {
                        element = document.RootElement.Clone();
                    }
                }
                catch (JsonException)
                {
                    return null;
                }

                break;
            default:
                return null;
        }

        // 字符串里套着的对象再解一层(模型双重编码时)
        if (element.ValueKind == JsonValueKind.String)
        {
            return ReadObject(new AIFunctionArguments { [key] = element.GetString() }, key);
        }

        if (element.ValueKind != JsonValueKind.Object) return null;
        Dictionary<string, JsonElement> result = new();
        foreach (JsonProperty property in element.EnumerateObject()) result[property.Name] = property.Value.Clone();
        return result;
    }
}
