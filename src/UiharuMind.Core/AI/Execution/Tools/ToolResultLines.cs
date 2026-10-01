namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 工具结果里按行前缀写、按行前缀读的那些行（如生图的 <c>Saved: </c>、看图的 <c>Preview: </c>）。
/// 前缀由各工具自己定，写与读都在那个工具里；这里只放共用的拆行
/// </summary>
internal static class ToolResultLines
{
    /// <summary>
    /// 取出所有以某个前缀开头的行，去掉前缀与首尾空白
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <param name="prefix">行前缀</param>
    /// <returns>行内容，按出现顺序</returns>
    public static IReadOnlyList<string> Parse(string? result, string prefix)
    {
        if (string.IsNullOrEmpty(result)) return [];
        return result.Split('\n')
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Trim())
            .ToList();
    }
}
