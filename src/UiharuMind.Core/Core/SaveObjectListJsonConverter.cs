using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UiharuMind.Core.Core;

/// <summary>
/// 角色卡 <see cref="UiharuMind.Core.AI.Character.CharacterCards.Data.AlternateGreetings"/> 等
/// <c>List&lt;object&gt;</c> 字段的单点 converter（主题五拍板：语义照抄 ArgumentsJsonConverter 的值分派）。
/// 行为等价反射基线（SaveUtility.JsonOptions 带 UnknownTypeHandling=JsonNode）：
///   写：元素按运行时类型写（string/数字/bool/JsonNode/JsonElement/嵌套列表/嵌套字典/null）；
///   读：元素还原成 <see cref="JsonNode"/>（JsonValue）——反射基线 object 值在 JsonNode 容错下就是 JsonValue，
///   消费端（CharacterCardImporter.CoerceGreetings）按 JsonValue 形态处理，null → C# null。
/// 只挂这两个属性，不全局动 object。
/// </summary>
internal sealed class SaveObjectListJsonConverter : JsonConverter<List<object?>>
{
    public override List<object?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a JSON array");
        }

        List<object?> result = new();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType == JsonTokenType.Null)
            {
                result.Add(null);
                continue;
            }

            result.Add(JsonNode.Parse(ref reader));
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (object? item in value)
        {
            WriteObjectValue(writer, item);
        }

        writer.WriteEndArray();
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
            case JsonNode node:
                node.WriteTo(writer);
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
            case System.Collections.IDictionary dictionary:
                writer.WriteStartObject();
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    writer.WritePropertyName(entry.Key?.ToString() ?? "");
                    WriteObjectValue(writer, entry.Value);
                }

                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable enumerable:
                writer.WriteStartArray();
                foreach (object? item in enumerable)
                {
                    WriteObjectValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                // 未知运行时类型：显式失败，优于静默写成错误形状。
                // 外部角色卡常见值（字符串/对象/数组）都覆盖了，护栏有代表样本兜底。
                throw new NotSupportedException($"Unsupported list element type: {value.GetType().FullName}");
        }
    }
}