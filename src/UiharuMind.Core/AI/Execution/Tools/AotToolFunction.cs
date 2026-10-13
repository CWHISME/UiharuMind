using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// AOT 安全的工具函数基类（A1 尖刺）：手写 AIFunction 子类，绕过 M.E.AI 的
/// <see cref="AIFunctionFactory.Create(MethodInfo, object?, AIFunctionFactoryOptions)"/> 反射路径
/// （AOT 下所有 Create 重载的 ReflectionAIFunction.Build 都做反射参数 marshaller，禁用）。
/// 子类只做三件事：显式声明 JsonSchema、override Name/Description、给一个 handler 委托。
/// 参数反序列化由 <see cref="Marshaller"/> 走 JsonElement→目标类型的显式映射，零反射。
/// </summary>
internal sealed class AotToolFunction : AIFunction
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;
    private readonly Func<AIFunctionArguments, CancellationToken, Task<object?>> _handler;

    public AotToolFunction(
        string name,
        string description,
        JsonElement jsonSchema,
        Func<AIFunctionArguments, CancellationToken, Task<object?>> handler)
    {
        _name = name;
        _description = description;
        _jsonSchema = jsonSchema;
        _handler = handler;
    }

    public override string Name => _name;

    public override string Description => _description;

    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        return await _handler(arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A1.5：绕过 AIFunction.InvokeAsync 的返回值反射序列化（EmptyJsonTypeInfoResolver），
    /// 供自定义 FunctionInvoker 直接调 handler。InvokeAsync 便捷重载在 AOT 下不可用。
    /// </summary>
    public ValueTask<object?> InvokeDirect(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        return InvokeCoreAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// 把手写的 JSON schema（JsonNode）转成 JsonElement。不走 STJ 类型图（JsonNode 自序列化，AOT 安全）
    /// </summary>
    public static JsonElement SchemaFromNode(JsonNode node)
    {
        using JsonDocument doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// 通用参数 marshaller：从 AIFunctionArguments（IDictionary&lt;string, object?&gt;，值多为 JsonElement）
    /// 读取并转换到目标类型。目前覆盖 string/int/int?/bool/bool?，按需扩展——增量成本就是加一行 switch 分支。
    /// </summary>
    public static class Marshaller
    {
        public static string? String(AIFunctionArguments args, string key)
        {
            if (!args.TryGetValue(key, out object? value) || value == null) return null;
            return value is string s ? s : ((JsonElement)value).GetString();
        }

        public static int Int(AIFunctionArguments args, string key, int defaultValue)
        {
            if (!args.TryGetValue(key, out object? value) || value == null) return defaultValue;
            return value is int i ? i : ((JsonElement)value).GetInt32();
        }

        public static int? NullableInt(AIFunctionArguments args, string key)
        {
            if (!args.TryGetValue(key, out object? value) || value == null) return null;
            return value is int i ? i : ((JsonElement)value).GetInt32();
        }

        public static bool Bool(AIFunctionArguments args, string key, bool defaultValue)
        {
            if (!args.TryGetValue(key, out object? value) || value == null) return defaultValue;
            return value is bool b ? b : ((JsonElement)value).GetBoolean();
        }

        /// <summary>
        /// Edit 的 edits 参数：JsonElement 数组 → List&lt;FileEdit&gt;。属性名兼容两种拼写，
        /// 零反射（手写解析，替代反射版 LenientFileEditListConverter）
        /// </summary>
        public static List<UiharuMind.Core.AI.Execution.Files.FileEdit> FileEdits(AIFunctionArguments args, string key)
        {
            if (!args.TryGetValue(key, out object? value) || value == null) return [];
            JsonElement root = value is JsonElement e ? e : JsonElement.Parse(value.ToString()!);
            if (root.ValueKind != JsonValueKind.Array) return [];

            List<UiharuMind.Core.AI.Execution.Files.FileEdit> result = new();
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string oldString = Property(item, "oldString") ?? Property(item, "OldString") ?? string.Empty;
                string newString = Property(item, "newString") ?? Property(item, "NewString") ?? string.Empty;
                result.Add(new UiharuMind.Core.AI.Execution.Files.FileEdit { OldString = oldString, NewString = newString });
            }

            return result;
        }

        private static string? Property(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }
}