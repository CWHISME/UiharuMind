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
              "description": "可选。扩展名；省略则列出全部扩展及其是否就绪。"
            },
            "command": {
              "type": "string",
              "description": "可选。该扩展的一条命令；给定时返回它的完整描述和参数。"
            }
          }
        }
        """;

    // 插值引用工具名常量（$$ 分隔符让 JSON 的花括号原样保留）；不能是 const，所以用 static
    private static string CallSchema = $$"""
        {
          "type": "object",
          "properties": {
            "extension": {
              "type": "string",
              "description": "扩展名，以 {{ExtensionCalls.HelpName}} 列出的为准。"
            },
            "command": {
              "type": "string",
              "description": "命令名，与 {{ExtensionCalls.HelpName}} 显示的完全一致。"
            },
            "arguments": {
              "type": "object",
              "description": "只放该命令自己的参数，JSON 对象；`extension` 和 `command` 绝不放在这里。无参数的命令可省略。",
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
            "查询扩展：不在你的工具列表里的能力——内置的（真实浏览器、图像生成、音频）以及用户通过 MCP 连接的应用。" +
            "不传参数：列出所有扩展及其就绪状态。传 `extension`：返回其用法说明和命令。" +
            "传 `extension` 和 `command`：返回该命令的完整描述和参数。" +
            "集合是动态的：用户后来接入的 MCP 应用会出现在这里，名单里没有的先查一次再下结论。",
            HelpSchema,
            (arguments, ct) => InvokeHelpAsync(hub, arguments, ct)),
        new ApprovalRequiredAIFunction(new MetaFunction(ExtensionCalls.CallName,
            "运行一条扩展命令。内置命令列在下面；MCP 扩展先查 " + ExtensionCalls.HelpName + "。" +
            "结果与帮助里命令写作 extension.command（如 browser.snapshot）。" +
            "大结果会保存到文件，只返回头尾；其余读保存的文件。\n\n" +
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
        ExtensionCallParts parts = ExtensionCalls.ReadParts(arguments);
        if (parts.Arguments == null && !parts.Unwrapped && HasValue(arguments, "arguments"))
        {
            return "参数 `arguments` 必须是 JSON 对象，如 {\"url\": \"https://example.com\"}。什么都没运行。";
        }

        object? result = await hub.CallAsync(parts.Extension, parts.Command, parts.Arguments, cancellationToken)
            .ConfigureAwait(false);
        if (!parts.Unwrapped) return result;

        // 照做了，但要让模型知道写法错了，下次写对
        string notice = $"（注意：`extension`、`command` 应放在顶层，这次已按 {parts.Extension}.{parts.Command} 理解。）\n";
        return result switch
        {
            string text => notice + text,
            JsonElement { ValueKind: JsonValueKind.String } element => notice + element.GetString(),
            IEnumerable<AIContent> contents => contents.Prepend(new TextContent(notice)).ToList(),
            null => notice.TrimEnd(),
            _ => notice + result,
        };
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
