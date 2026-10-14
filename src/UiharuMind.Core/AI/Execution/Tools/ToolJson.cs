/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using System.Text.Json.Serialization.Metadata;
using UiharuMind.Core.Core.Extensions;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 数组参数的宽容读法：schema 说要数组，模型给了一个标量字符串时也照收。
///
/// 实机踩到的就是这个：<c>Grep(fileGlobs: "*.cpp")</c> 直接死在反序列化上，
/// 模型拿回的是 <c>The JSON value could not be converted to System.String[]</c>——
/// 一句它既看不懂也无从改正的框架异常（连哪个参数错了都没说）。
/// 「该给数组却给了标量」是模型的通病，不是这一个参数的问题，所以治在序列化层。
/// </summary>
public sealed class LenientStringArrayConverter : JsonConverter<string[]>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            string value = reader.GetString() ?? string.Empty;
            if (value.Length == 0) return [];
            return TryParseEmbeddedArray(value) ?? SplitLoosely(value);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a string or an array of strings, got {reader.TokenType}.");
        }

        List<string> items = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            // 数组里混进非字符串(数字、布尔)时按其字面量收下,不为此整调用失败:
            // 一条不合规的 glob 顶多匹配不到,而整调用失败要多烧一轮
            items.Add(reader.TokenType == JsonTokenType.String
                ? reader.GetString() ?? string.Empty
                : JsonDocument.ParseValue(ref reader).RootElement.ToString());
        }

        return items.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    // 整个数组被转义成一段 JSON 字符串（"[\"*.cs\"]"）：实机踩到过，按字面当 glob 只会静默 0 命中。
    // 解析不成 JSON 的（如 glob 字符类 "[ab]"）原样交回
    private static string[]? TryParseEmbeddedArray(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']') return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            List<string> items = [];
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                items.Add(element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString());
            }

            return items.ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 一个标量字符串里塞了多项时按逗号拆开（<c>"*.cpp,*.h"</c>）。
    ///
    /// <b>带花括号的一律不拆</b>：glob 的分组语法本身就用逗号（<c>*.{cpp,h}</c>），
    /// 拆了会把一个写对了的表达式毁掉。
    /// </summary>
    private static string[] SplitLoosely(string value)
    {
        if (value.Contains('{')) return [value];

        string[] parts = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [value] : parts;
    }
}

/// <summary>
/// <c>List&lt;FileEdit&gt;</c> 的宽容读法：schema 说要数组，模型却给了单对象或一整段 JSON 字符串时也照收。
///
/// 实机踩到的三种形态（与 pi 的 <c>prepareArguments</c> 兼容层处理的是同一类模型怪癖）：
/// ✓ <c>edits</c> 整个传成一段 JSON 字符串（里面可能又套数组或单对象）；
/// ✓ <c>edits</c> 漏掉外层数组，直接传一个编辑对象；
/// ✓ 元素属性名写成 <c>old_string</c>/<c>new_string</c>（underscore 旧习惯）。
/// 属性名读取时手工兼容两种拼写，不依赖序列化选项。
/// </summary>
public sealed class LenientFileEditListConverter : JsonConverter<List<FileEdit>>
{
    public override List<FileEdit>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            // 形态一：整个 edits 是一段 JSON 字符串——先按 JSON 解析一次再看是数组还是对象。
            // 模型把 edits 发成一段根本不像 JSON 的普通文本（"replace foo with bar"）时,
            // Parse 会抛底层 JsonException——那正是我们本批要消灭的"模型看不懂也无从改正"的失败。
            // 只在这里把"JSON 解析失败"转成话术;ReadElements 自己抛的语义异常(标量根/非对象元素)
            // 必须原样穿透,不能被这个 catch 吞掉改写成"non-JSON string"——那是另一个错误。
            string raw = reader.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return [];

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(raw);
            }
            catch (JsonException ex)
            {
                throw new JsonException(
                    $"edits 必须是 {{oldString,newString}} 对象的 JSON 数组、单个这样的对象，"
                    + $"或包含其中之一的 JSON 字符串。拿到的是非 JSON 字符串：'{Preview(raw)}'。", ex);
            }

            using (doc)
            {
                return ReadElements(doc.RootElement);
            }
        }

        if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
        {
            throw new JsonException($"期望 edits 是对象或数组，拿到的是 {reader.TokenType}。");
        }

        using JsonDocument top = JsonDocument.ParseValue(ref reader);
        return ReadElements(top.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, List<FileEdit> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (FileEdit edit in value)
        {
            writer.WriteStartObject();
            writer.WriteString("oldString", edit.OldString);
            writer.WriteString("newString", edit.NewString);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static List<FileEdit> ReadElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            // 形态二：漏了数组，单对象 → 包成单元素数组
            return [ReadEdit(root)];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                $"edits 必须是 {{oldString,newString}} 对象的数组或单个这样的对象，"
                + $"拿到的是 {root.ValueKind}。");
        }

        List<FileEdit> edits = new(root.GetArrayLength());
        foreach (JsonElement item in root.EnumerateArray())
        {
            edits.Add(ReadEdit(item));
        }

        return edits;
    }

    private static FileEdit ReadEdit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"每个 edit 必须是带 oldString 和 newString 的对象，拿到的是 {element.ValueKind}。");
        }

        return new FileEdit
        {
            // 形态三：属性名兼容 underscore 拼写
            OldString = GetString(element, "oldString") ?? GetString(element, "old_string") ?? string.Empty,
            NewString = GetString(element, "newString") ?? GetString(element, "new_string") ?? string.Empty,
        };
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Preview(string s) => s.Length <= 40 ? s : s[..40] + "…";
}

/// <summary>
/// 工具参数与返回值里框架默认口径之外的类型。AOT 下 <see cref="AIJsonUtilities.DefaultOptions"/>
/// 只认框架自带的 source-gen 类型，缺了这里的声明，<see cref="AIFunctionFactory"/> 装配时就拿不到类型信息。
/// 新工具用了新的参数或返回类型，在这里补一行。
/// </summary>
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<FileEdit>))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(float?))]
[JsonSerializable(typeof(double?))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;

/// <summary>
/// 工具调用参数的序列化口径。<b>基于框架默认那份复制</b>再加转换器——
/// 直接改 <see cref="AIJsonUtilities.DefaultOptions"/> 是全局改动，会波及所有工具与结构化输出。
/// </summary>
public static class ToolJson
{
    /// <summary>
    /// 工具调用参数的紧凑写法：折叠历史、审批提示这类给模型看的地方用
    /// </summary>
    public static JsonTypeInfo<IDictionary<string, object?>> Arguments { get; } = CreateArgumentsInfo();

    /// <summary>推 schema 用的口径：类型信息与 <see cref="Lenient"/> 相同，但不挂转换器</summary>
    private static readonly JsonSerializerOptions SchemaInference = CreateOptions(withConverters: false);

    /// <summary>宽容口径：数组参数也接受标量字符串。其余与框架默认的 source-gen 口径一致</summary>
    public static JsonSerializerOptions Lenient { get; } = CreateOptions(withConverters: true);

    /// <summary>
    /// 工具参数的 schema 口径。挂了自定义转换器的类型 schema 生成器推不出结构，只会给无类型的空 schema
    /// （实机：Edit 的 edits 丢了 array/items，模型连 newString 都不知道），这里对这些类型改用框架默认口径推一遍。
    /// </summary>
    public static AIJsonSchemaCreateOptions Schema { get; } = new() { TransformSchemaNode = RestoreConvertedSchema };

    /// <summary>
    /// 工具工厂选项：参数解析走 <see cref="Lenient"/>，schema 走 <see cref="Schema"/>。两者必须成对，只挂前者会吞掉 schema
    /// </summary>
    /// <param name="name">工具名</param>
    /// <param name="description">工具说明；为空时取方法上的 Description</param>
    /// <returns>新的工厂选项</returns>
    public static AIFunctionFactoryOptions CreateFactoryOptions(string name, string? description = null) => new()
    {
        Name = name,
        Description = description,
        SerializerOptions = Lenient,
        JsonSchemaCreateOptions = Schema,
    };

    /// <summary>
    /// 按 <see cref="CreateFactoryOptions"/> 的口径把委托包成工具。所有自建工具都从这里造，参数解析与 schema 才一致
    /// </summary>
    /// <param name="method">工具实现</param>
    /// <param name="name">工具名</param>
    /// <param name="description">工具说明；为空时取方法上的 Description</param>
    /// <returns>工具函数</returns>
    public static AIFunction CreateFunction(Delegate method, string name, string? description = null)
    {
        EnsureAotSafe(method, name);
        return AIFunctionFactory.Create(method, CreateFactoryOptions(name, description));
    }

    /// <summary>
    /// 漏声明的 AOT 雷在开发期就炸：参数/返回类型既不在框架 source-gen、又没在 <see cref="ToolJsonContext"/>
    /// 声明时，<see cref="SchemaInference"/>（剥了反射兜底、与 AOT 同路）取不到 metadata，这里直接抛。
    /// 不抛的话 R2R 全绿、发 AOT 包才静默挂——就是 7115e9d2 撤手写工具要防的那种漂移。
    /// </summary>
    private static void EnsureAotSafe(Delegate method, string name)
    {
        MethodInfo body = method.Method;
        List<Type> serializable =
        [
            .. body.GetParameters()
                  .Where(p => p.ParameterType != typeof(CancellationToken)) //控制参不参与序列化
                  .Select(p => p.ParameterType),
        ];
        Type ret = body.ReturnType;
        // 异步返回值序列化的是内层 T：Task<T>（class，IsAssignableTo Task）与 ValueTask<T>（struct，只能按泛型定义认）
        if (ret.IsGenericType
            && (ret.IsAssignableTo(typeof(Task)) || ret.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            serializable.Add(ret.GenericTypeArguments[0]);
        }
        else if (ret != typeof(Task) && ret != typeof(ValueTask) && ret != typeof(void))
        {
            serializable.Add(ret); //裸 Task/ValueTask/void 无可序列化结果
        }

        foreach (Type type in serializable.Distinct())
        {
            if (!SchemaInference.TryGetTypeInfo(type, out _))
            {
                throw new InvalidOperationException(
                    $"工具 '{name}' 的 {type.FullName} 既不在框架 source-gen 也没在 ToolJsonContext 声明——"
                    + "AOT 产物会在此静默挂。去 ToolJsonContext 补一行 [JsonSerializable(typeof(...))]。" +
                    "（确属控制参且不参与序列化，别加进 serializable。）");
            }
        }
    }

    private static JsonTypeInfo<IDictionary<string, object?>> CreateArgumentsInfo()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };
        options.MakeReadOnly();
        return options.GetTypeInfo<IDictionary<string, object?>>();
    }

    private static JsonNode RestoreConvertedSchema(AIJsonSchemaCreateContext context, JsonNode node)
    {
        Type type = context.TypeInfo.Type;
        if (node is not JsonObject original || !Lenient.Converters.Any(c => c.Type == type)) return node;

        JsonElement inferred = AIJsonUtilities.CreateJsonSchema(type, serializerOptions: SchemaInference);
        if (JsonNode.Parse(inferred.GetRawText()) is not JsonObject restored) return node;

        // 参数自己的 description/default 留在原节点上，推出来的结构里没有
        foreach ((string key, JsonNode? value) in original)
        {
            if (!restored.ContainsKey(key)) restored[key] = value?.DeepClone();
        }

        // 可空性挂在参数上，按裸类型推出来的结构带不过来；默认值是 null 却不许 null，schema 就自相矛盾了
        if (original.TryGetPropertyValue("default", out JsonNode? defaultValue) && defaultValue is null
            && restored["type"] is JsonValue typeValue && typeValue.TryGetValue(out string? typeName)
            && typeName != "null")
        {
            restored["type"] = new JsonArray(typeName, "null");
        }

        return restored;
    }

    /// <summary>
    /// 只认 source-gen 类型：框架默认那份在非 AOT 下还挂着反射兜底（解析器链尾与枚举转换器），这里拿掉，
    /// 让测试与 AOT 走同一条路，漏声明的类型在单元测试里就会炸，而不是等到 AOT 产物里
    /// </summary>
    private static JsonSerializerOptions CreateOptions(bool withConverters)
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Clear();
        options.TypeInfoResolverChain.Add(ToolJsonContext.Default);
        options.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolverChain[0]);
        options.Converters.Clear();
        if (withConverters)
        {
            options.Converters.Add(new LenientStringArrayConverter());
            options.Converters.Add(new LenientFileEditListConverter());
        }

        options.MakeReadOnly();
        return options;
    }
}
