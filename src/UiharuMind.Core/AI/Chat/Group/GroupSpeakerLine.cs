namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群发言带发言人的那一行：「[名字]: 正文」。拼（交给模型时）与拆（呈现、剥模型自加的前缀）都在这里，格式只有一份。
/// 拆的时候宽一点，认模型常见的仿写：「【名字】：」、括号与冒号之间有空格；裸名式「名字：」单独认，见 <see cref="TryReadBare"/>
/// </summary>
public static class GroupSpeakerLine
{
    /// <summary>
    /// 拼一行：发言人前缀 + 正文
    /// </summary>
    /// <param name="speaker">发言人显示名；为空时写「?」</param>
    /// <param name="body">正文</param>
    /// <returns>带发言人前缀的一段</returns>
    public static string Format(string? speaker, string body) =>
        $"[{(string.IsNullOrWhiteSpace(speaker) ? "?" : speaker)}]: {body}";

    /// <summary>
    /// 拆括号式前缀：「[名]:」「[名]：」「【名】:」「【名】：」，括号与冒号之间允许空格
    /// </summary>
    /// <param name="text">以前缀开头的正文</param>
    /// <param name="name">括号里的名字</param>
    /// <param name="rest">冒号之后的正文（去掉开头空白）</param>
    /// <returns>是这个格式为 true</returns>
    public static bool TryReadBracketed(string text, out string name, out string rest)
    {
        name = string.Empty;
        rest = string.Empty;
        if (text.Length == 0 || (text[0] != '[' && text[0] != '【')) return false;

        char close = text[0] == '[' ? ']' : '】';
        int end = text.IndexOf(close);
        if (end <= 1) return false;

        string after = text[(end + 1)..].TrimStart();
        if (after.Length == 0 || (after[0] != ':' && after[0] != '：')) return false;

        name = text[1..end].Trim();
        rest = after[1..].TrimStart();
        return true;
    }

    /// <summary>
    /// 拆裸名式前缀「名:」「名：」。只认给定的名字：裸写的「谁：」在句首多半是在跟人说话，不能见冒号就拆
    /// </summary>
    /// <param name="text">正文</param>
    /// <param name="name">认的名字</param>
    /// <param name="rest">冒号之后的正文</param>
    /// <returns>是这个格式为 true</returns>
    public static bool TryReadBare(string text, string name, out string rest)
    {
        rest = string.Empty;
        if (name.Length == 0 || !text.StartsWith(name, StringComparison.Ordinal)) return false;

        string after = text[name.Length..].TrimStart();
        if (after.Length == 0 || (after[0] != ':' && after[0] != '：')) return false;

        rest = after[1..].TrimStart();
        return true;
    }
}
