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
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// <see cref="ExtensionCalls.HelpName"/> 的渲染：纯函数，模型看到的文本形态在这里钉死。内置扩展与 MCP server 共用。
///
/// 两层输出对应渐进披露：只带扩展名 → 用法说明 + 命令名与一行说明；
/// 再带命令名 → 该命令的完整描述与参数 schema。100 个工具的 server 不会一次灌满上下文。
/// </summary>
internal static class McpHelpFormatter
{
    /// 清单里每个工具说明的截断长度；完整描述留给带 tool 的那一层
    private const int SummaryChars = 160;

    /// 报"有哪些工具"时最多列几个名字，免得一次报错就是几千 token
    private const int MaxListedNames = 40;

    /// 工具不超过这个数就在报错里全列（一眼看完，比让它再查一次划算）
    private const int MaxSuggestAll = 8;

    /// 工具更多时最多建议几个最像的
    private const int MaxSuggestClose = 5;

    /// <summary>
    /// 渲染扩展概览。
    /// </summary>
    /// <param name="serverName">扩展名</param>
    /// <param name="instructions">用法说明（MCP 即 server 自述）；空则省略</param>
    /// <param name="tools">命令定义</param>
    /// <param name="note">前置说明（如"离线，以下来自缓存"）；空则省略</param>
    /// <returns>给模型的文本</returns>
    public static string RenderServer(string serverName, string instructions,
        IReadOnlyList<McpToolDescriptor> tools, string? note = null)
    {
        StringBuilder sb = new();
        sb.Append("# ").Append(serverName).Append('\n');
        if (!string.IsNullOrEmpty(note)) sb.Append('\n').Append(note).Append('\n');
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            sb.Append('\n').Append(instructions.Trim()).Append('\n');
        }

        sb.Append("\n命令（").Append(tools.Count).Append("）：\n");
        foreach (McpToolDescriptor tool in tools)
        {
            sb.Append("- ").Append(tool.Name).Append(' ').Append(Signature(tool.InputSchema));
            string summary = Summarize(tool.Description);
            if (summary.Length > 0) sb.Append(": ").Append(summary);
            sb.Append('\n');
        }

        if (tools.Count > 0)
        {
            sb.Append("\n用 ").Append(ExtensionCalls.HelpName).Append(" 传 `name`（扩展名.命令名）可查看某条命令的完整参数。");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 渲染单条命令的详情。
    /// </summary>
    /// <param name="serverName">扩展名</param>
    /// <param name="tool">命令定义</param>
    /// <param name="note">前置说明；空则省略</param>
    /// <returns>给模型的文本</returns>
    public static string RenderTool(string serverName, McpToolDescriptor tool, string? note = null)
    {
        StringBuilder sb = new();
        sb.Append("# ").Append(serverName).Append('.').Append(tool.Name).Append('\n');
        if (!string.IsNullOrEmpty(note)) sb.Append('\n').Append(note).Append('\n');
        if (!string.IsNullOrWhiteSpace(tool.Description))
        {
            sb.Append('\n').Append(tool.Description.Trim()).Append('\n');
        }

        sb.Append("\n参数（JSON Schema）：\n");
        sb.Append(tool.InputSchema.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : ToolTokenEstimator.CompactSchema(tool.InputSchema));
        return sb.ToString();
    }

    /// <summary>
    /// 在工具清单里按名字找：先精确，再忽略大小写，最后忽略分隔符（<c>Game_Object-Create</c> 即
    /// <c>gameobject-create</c>——模型常把 kebab 与 snake 写混）。第三档只在<b>唯一</b>命中时采用，
    /// 有歧义宁可报未命中，也不替模型挑一个可能有副作用的工具。
    /// </summary>
    /// <typeparam name="T">条目类型</typeparam>
    /// <param name="items">清单</param>
    /// <param name="nameOf">取名字</param>
    /// <param name="name">模型给的名字</param>
    /// <returns>命中项；没有为 default</returns>
    public static T? FindByName<T>(IEnumerable<T> items, Func<T, string> nameOf, string name)
    {
        List<T> list = items as List<T> ?? items.ToList();
        T? exact = list.FirstOrDefault(x => string.Equals(nameOf(x), name, StringComparison.Ordinal));
        if (exact != null) return exact;
        T? caseless = list.FirstOrDefault(x => string.Equals(nameOf(x), name, StringComparison.OrdinalIgnoreCase));
        if (caseless != null) return caseless;

        string squashed = Squash(name);
        List<T> loose = list.Where(x => Squash(nameOf(x)) == squashed).ToList();
        return loose.Count == 1 ? loose[0] : default;
    }

    /// <summary>
    /// 未命中时给模型报"你是不是想调这些"。工具少就全列；多的时候<b>只给真正的近似拼写</b>。
    ///
    /// 只认拼写近似（忽略大小写与 <c>-</c>/<c>_</c> 分隔符后相等，或编辑距离很小）。
    /// 曾经按名字分段重合打分，结果 <c>editor-application-get-state</c> 被建议了一串只因含 <c>get</c>
    /// 的无关工具——真实原因往往是这个工具压根没开放，此时"相似"的建议只会误导。
    /// 完整清单在帮助里，这里不再重复倒一遍。
    /// </summary>
    /// <param name="names">全部可调用的名字</param>
    /// <param name="query">模型给的名字</param>
    /// <returns>逗号分隔的建议；没有近似拼写的返回空串</returns>
    public static string SuggestNames(IEnumerable<string> names, string query)
    {
        List<string> all = names.ToList();
        if (all.Count <= MaxSuggestAll) return ListNames(all);

        string wanted = Squash(query);
        int allowed = Math.Clamp(wanted.Length / 6, 1, 3);
        return string.Join(", ", all
            .Select((name, index) => (name, index, distance: EditDistance(Squash(name), wanted)))
            .Where(x => x.distance <= allowed)
            .OrderBy(x => x.distance).ThenBy(x => x.index)
            .Take(MaxSuggestClose).Select(x => x.name));
    }

    /// 去掉分隔符并统一大小写：<c>Game_Object-Create</c> 与 <c>gameobject-create</c> 是同一个名字的不同写法
    private static string Squash(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int EditDistance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 3) return int.MaxValue;
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int[] current = new int[b.Length + 1];
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[b.Length];
    }

    /// <summary>
    /// 未命中时给模型报"有哪些"。名字过多只列前若干个。
    /// </summary>
    /// <param name="names">全部名字</param>
    /// <returns>逗号分隔的清单文本</returns>
    public static string ListNames(IEnumerable<string> names)
    {
        List<string> all = names.ToList();
        if (all.Count == 0) return "（无）";
        string listed = string.Join(", ", all.Take(MaxListedNames));
        return all.Count > MaxListedNames ? $"{listed}, …（还有 {all.Count - MaxListedNames} 个）" : listed;
    }

    /// <summary>
    /// 参数签名：<c>{action, target?, value?}</c>，可选的带问号。概览里每条命令都带上，
    /// 模型不必再查一次详情就知道参数叫什么（不带时弱模型会照常识猜 <c>ref</c>、<c>text</c>）
    /// </summary>
    /// <param name="schema">参数 schema</param>
    /// <returns>签名；没有参数为 <c>{}</c></returns>
    public static string Signature(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object) return "{}";
        HashSet<string> required = RequiredOf(schema);
        IEnumerable<string> names = properties.EnumerateObject()
            .Select(x => required.Contains(x.Name) ? x.Name : x.Name + "?");
        return "{" + string.Join(", ", names) + "}";
    }

    /// <summary>
    /// schema 里的参数名
    /// </summary>
    /// <param name="schema">参数 schema</param>
    /// <returns>参数名；schema 没写 properties 时为 null（不知道有哪些，就别说谁多余）</returns>
    public static IReadOnlyCollection<string>? PropertyNames(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out JsonElement properties) &&
        properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(x => x.Name).ToList()
            : null;

    /// <summary>
    /// schema 里的必填参数名
    /// </summary>
    /// <param name="schema">参数 schema</param>
    /// <returns>必填参数名</returns>
    public static HashSet<string> RequiredOf(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out JsonElement required) &&
        required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToHashSet()
            : [];

    private static string Summarize(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;
        string firstLine = description.Trim().Split('\n')[0].Trim();
        return firstLine.Length <= SummaryChars ? firstLine : firstLine[..SummaryChars] + "…";
    }
}
