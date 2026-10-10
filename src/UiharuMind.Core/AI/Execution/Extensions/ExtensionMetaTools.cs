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

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 扩展的两把元工具（ADR 0079）。<b>定义恒定</b>：名字、描述、schema 都不随有哪些扩展、连没连上而变，
/// 缓存前缀因此稳定。帮助不审批；调用一律经审批中间件，放不放由 <see cref="ExtensionRisk"/> 逐条判
/// </summary>
internal static class ExtensionMetaTools
{
    private const string HelpSchema = """
        {
          "type": "object",
          "properties": {
            "extension": {
              "type": "string",
              "description": "Optional. An extension name; omit to list every extension and whether it is ready."
            },
            "command": {
              "type": "string",
              "description": "Optional. A command of that extension; when given, returns its full description and parameters."
            }
          }
        }
        """;

    private const string CallSchema = """
        {
          "type": "object",
          "properties": {
            "extension": {
              "type": "string",
              "description": "Extension name, as listed by ExtensionHelp."
            },
            "command": {
              "type": "string",
              "description": "Command name, exactly as shown by ExtensionHelp."
            },
            "arguments": {
              "type": "object",
              "description": "Only the command's own parameters, as a JSON object; `extension` and `command` never go in here. Omit for commands without parameters.",
              "additionalProperties": true
            }
          },
          "required": ["extension", "command"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// 造两把元工具，共用同一个枢纽
    /// </summary>
    /// <param name="hub">绑定了本次装配的枢纽</param>
    /// <returns>帮助与调用（后者包了审批）</returns>
    public static IReadOnlyList<AITool> Create(ExtensionHub hub) =>
    [
        new MetaFunction(ExtensionCalls.HelpName,
            "Look up extensions: capabilities that are not in your tool list — built-in ones (a real browser, " +
            "image generation, audio) and apps the user connected over MCP. With no arguments: every extension " +
            "and whether it is ready. With `extension`: its usage notes and commands. With `extension` and " +
            "`command`: that command's full description and parameters. The set is dynamic: an app the user " +
            "starts later becomes available, so look again instead of assuming.",
            HelpSchema,
            (arguments, ct) => InvokeHelpAsync(hub, arguments, ct)),
        new ApprovalRequiredAIFunction(new MetaFunction(ExtensionCalls.CallName,
            "Run an extension command. The built-in ones are listed below; for an MCP extension, look it up with " +
            "ExtensionHelp first. Commands that can change something or cost money ask the user first. Results and " +
            "help refer to a command as extension.command (e.g. browser.snapshot). Large results are saved to a " +
            "file and only the head and tail are returned; read the saved file for the rest.\n\n" +
            BuiltinExtensions.CallUsage,
            CallSchema,
            (arguments, ct) => InvokeCallAsync(hub, arguments, ct))),
    ];

    private static async Task<object?> InvokeHelpAsync(ExtensionHub hub, AIFunctionArguments arguments,
        CancellationToken cancellationToken) =>
        await hub.HelpAsync(ExtensionCalls.ReadString(arguments, "extension"),
            ExtensionCalls.ReadString(arguments, "command"), cancellationToken).ConfigureAwait(false);

    // 参数对象给了却解不出来（JSON 写坏）就别执行：按「没参数」跑，全可选的命令会拿默认值真的去做一件事
    private static async Task<object?> InvokeCallAsync(ExtensionHub hub, AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement>? parsed = ExtensionCalls.ReadObject(arguments, "arguments");
        if (parsed == null && HasValue(arguments, "arguments"))
        {
            return "Parameter `arguments` must be a JSON object, e.g. {\"url\": \"https://example.com\"}. Nothing was run.";
        }

        return await hub.CallAsync(ExtensionCalls.ReadString(arguments, "extension"),
            ExtensionCalls.ReadString(arguments, "command"), parsed, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasValue(AIFunctionArguments arguments, string key) =>
        arguments.TryGetValue(key, out object? value) && value switch
        {
            null => false,
            string text => text.Trim().Length > 0,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => false,
            _ => true,
        };

    /// <summary>固定 schema 的 AIFunction：名字、描述、参数定义在构造时一次定死</summary>
    private sealed class MetaFunction : AIFunction
    {
        private readonly Func<AIFunctionArguments, CancellationToken, Task<object?>> _invoke;

        public MetaFunction(string name, string description, string schemaJson,
            Func<AIFunctionArguments, CancellationToken, Task<object?>> invoke)
        {
            Name = name;
            Description = description;
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            JsonSchema = document.RootElement.Clone();
            _invoke = invoke;
        }

        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema { get; }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            await _invoke(arguments, cancellationToken).ConfigureAwait(false);
    }
}
