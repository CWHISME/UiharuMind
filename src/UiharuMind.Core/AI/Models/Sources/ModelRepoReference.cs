namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 从用户输入认出仓库：<c>owner/repo</c>，或 HF / 镜像 / 魔搭的仓库链接
/// </summary>
public static class ModelRepoReference
{
    // HF 上与模型仓库同级、但不是模型的路径
    private static readonly string[] NonModelPrefixes = ["datasets", "spaces", "collections", "docs", "api"];

    /// <summary>
    /// 尝试把输入解析成仓库
    /// </summary>
    /// <param name="input">搜索框输入</param>
    /// <param name="repository">owner/repo</param>
    /// <returns>是仓库返回 true；是普通关键词返回 false</returns>
    public static bool TryParse(string? input, out string repository)
    {
        repository = "";
        string text = input?.Trim() ?? "";
        if (text.Length == 0 || text.Any(char.IsWhiteSpace)) return false;

        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            return TryFromSegments(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries), out repository);

        string[] parts = text.Split('/');
        if (parts.Length != 2 || !IsName(parts[0]) || !IsName(parts[1])) return false;
        repository = text;
        return true;
    }

    private static bool TryFromSegments(string[] segments, out string repository)
    {
        repository = "";
        int start = 0;
        // 魔搭：/models/owner/repo
        if (segments.Length > 0 && segments[0] == "models") start = 1;
        else if (segments.Length > 0 && NonModelPrefixes.Contains(segments[0])) return false;
        if (segments.Length - start < 2) return false;

        string owner = Uri.UnescapeDataString(segments[start]);
        string name = Uri.UnescapeDataString(segments[start + 1]);
        if (!IsName(owner) || !IsName(name)) return false;
        repository = $"{owner}/{name}";
        return true;
    }

    private static bool IsName(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
