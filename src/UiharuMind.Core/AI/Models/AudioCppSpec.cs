using System.Text.Json;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// audio.cpp 自家打包的 GGUF 内嵌的 model_spec（键 <c>audiocpp.model_spec.json</c>）。
/// 新包是完整 spec（含 category），老包只有 family，category 得从 family 后缀推断。见 ADR 0071。
/// </summary>
internal static class AudioCppSpec
{
    /// <summary>
    /// audio.cpp GGUF 的 general.architecture
    /// </summary>
    internal const string Architecture = "audiocpp";

    internal sealed record Info(string Category, string DisplayName, string Family);

    /// <summary>
    /// 解析内嵌 spec；拿不到或不是合法 JSON 返回 null
    /// </summary>
    internal static Info? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string family = GetString(root, "family");
            string category = GetString(root, "category");
            if (category.Length == 0) category = InferCategory(family);
            return new Info(category, GetString(root, "display_name"), family);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 老包 spec 没有 category 字段，按 family 名后缀推断（pocket_tts / moonshine_asr）
    /// </summary>
    internal static string InferCategory(string family)
    {
        if (family.EndsWith("_tts", StringComparison.OrdinalIgnoreCase)) return "tts";
        if (family.EndsWith("_asr", StringComparison.OrdinalIgnoreCase)) return "asr";
        return "";
    }

    private static string GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? ""
            : "";
    }
}
