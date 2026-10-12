using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// <see cref="Microsoft.Extensions.AI.FunctionCallContent.Arguments"/> 的单点序列化 converter
/// （用户拍板：只挂这一个属性，绝不全局动 object——后者会波及 SaveUtility 的 object 路由，A2 那摊）。
/// 行为等价反射模式基线（探针实测）：
///   写：值按运行时类型写（int 写数字、bool 写布尔、嵌套字典/数组递归）；
///   读：值一律还原成 <see cref="JsonElement"/>（反射模式反序列化 object 本来就是 JsonElement）。
/// 形状与语义由 SessionJsonShapeGuardTests 两层护栏兜底。
/// </summary>
internal sealed class ArgumentsJsonConverter : JsonConverter<IDictionary<string, object?>>
{
    public override IDictionary<string, object?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Arguments must be a JSON object");
        }

        Dictionary<string, object?> result = new();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Unexpected token while reading Arguments");
            }

            string key = reader.GetString()!;
            reader.Read();
            JsonElement element = JsonElement.ParseValue(ref reader);
            // 反射模式基线：顶层 null 值反序列化成 C# null（不是 JsonElement(Null)），必须复刻
            result[key] = element.ValueKind == JsonValueKind.Null ? null : element;
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, IDictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> pair in value)
        {
            writer.WritePropertyName(pair.Key);
            WriteObjectValue(writer, pair.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteObjectValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case IDictionary<string, object?> nested:
                WriteNestedObject(writer, nested);
                break;
            case System.Collections.IDictionary nested:
                // 带类型的字典（如 Dictionary<string,string>）也是对象形态，不能掉进 IEnumerable 写成 KeyValuePair 数组
                WriteNestedIDictionary(writer, nested);
                break;
            case System.Collections.IEnumerable enumerable:
                WriteArray(writer, enumerable);
                break;
            default:
                // 未知运行时类型：显式失败，优于静默写成错误形状。
                // 实际工具参数到不了这里（都是 JSON 兼容值），护栏有代表样本兜底。
                throw new NotSupportedException($"Unsupported argument value type: {value.GetType().FullName}");
        }
    }

    private static void WriteNestedObject(Utf8JsonWriter writer, IDictionary<string, object?> nested)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> pair in nested)
        {
            writer.WritePropertyName(pair.Key);
            WriteObjectValue(writer, pair.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteNestedIDictionary(Utf8JsonWriter writer, System.Collections.IDictionary nested)
    {
        writer.WriteStartObject();
        foreach (System.Collections.DictionaryEntry entry in nested)
        {
            writer.WritePropertyName(entry.Key?.ToString() ?? "");
            WriteObjectValue(writer, entry.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, System.Collections.IEnumerable enumerable)
    {
        writer.WriteStartArray();
        foreach (object? item in enumerable)
        {
            WriteObjectValue(writer, item);
        }

        writer.WriteEndArray();
    }
}