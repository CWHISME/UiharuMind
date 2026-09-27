namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>群成员名单里的一项：会话标识 + 显示名</summary>
/// <param name="SessionId">成员会话标识</param>
/// <param name="Name">显示名</param>
public readonly record struct GroupRosterEntry(string SessionId, string Name);

/// <summary>
/// 文本 @ 路由（ADR 0049 决策 5）：`@名字` 字面留在正文里，这里只认出点到了谁。
/// `@` 前紧贴字母数字的不算点名（邮箱写法如 a@alice.com）
/// </summary>
public static class GroupMentions
{
    /// <summary>
    /// 认出正文里 @ 到的成员。名字从长到短比，免得短名吃掉以它开头的长名；全角 ＠ 也认
    /// </summary>
    /// <param name="text">正文</param>
    /// <param name="roster">成员名单</param>
    /// <returns>被 @ 的成员会话标识，按首次出现的顺序、不重复</returns>
    public static IReadOnlyList<string> Parse(string? text, IReadOnlyList<GroupRosterEntry> roster)
    {
        if (string.IsNullOrEmpty(text) || roster.Count == 0) return [];

        List<GroupRosterEntry> byLength = roster
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderByDescending(x => x.Name.Length)
            .ToList();
        List<string> mentioned = [];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '@' && text[i] != '＠') continue;
            // @ 紧跟在字母数字后面不算点名：a@alice.com 是邮箱写法（与补全 TryFindMention 同口径）
            if (i > 0 && char.IsAsciiLetterOrDigit(text[i - 1])) continue;

            ReadOnlySpan<char> rest = text.AsSpan(i + 1);
            foreach (GroupRosterEntry entry in byLength)
            {
                if (!rest.StartsWith(entry.Name.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;

                if (!mentioned.Contains(entry.SessionId)) mentioned.Add(entry.SessionId);
                i += entry.Name.Length;
                break;
            }
        }

        return mentioned;
    }
}
