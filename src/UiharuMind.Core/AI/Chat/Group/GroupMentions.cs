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
    /// 认出正文里 @ 到的成员。叫法按 <see cref="GroupSpeakerName"/>：全名、括号前的本名、括号里的别名（如「五更琉璃（黑猫）」），
    /// 以及其中连续的两字起（@琉璃、@千空）；完整叫法优先，部分叫法两人共用的不算点到谁。
    /// 同一处取最长的叫法，免得短名吃掉以它开头的长名；全角 ＠ 也认。
    /// 名单在场景段里是分组式写法（「《死亡笔记》：L」），模型与用户常照抄进 @（「@《死亡笔记》：L」），
    /// 直接匹配失败时剥掉前导的作品名前缀再认一次，否则点到的人一次都叫不醒
    /// </summary>
    /// <param name="text">正文</param>
    /// <param name="roster">成员名单</param>
    /// <returns>被 @ 的成员会话标识，按首次出现的顺序、不重复</returns>
    public static IReadOnlyList<string> Parse(string? text, IReadOnlyList<GroupRosterEntry> roster)
    {
        if (string.IsNullOrEmpty(text) || roster.Count == 0) return [];

        GroupSpeakerNames<string> names = new(roster.Select(x => (x.SessionId, x.Name)));
        List<string> mentioned = [];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '@' && text[i] != '＠') continue;
            // @ 紧跟在字母数字后面不算点名：a@alice.com 是邮箱写法（与补全 TryFindMention 同口径）
            if (i > 0 && char.IsAsciiLetterOrDigit(text[i - 1])) continue;
            if (!names.TryMatchStart(text.AsSpan(i + 1), out string sessionId, out int length))
            {
                // 直接匹配失败才走回退：已经认得出的不受影响
                if (!TryMatchAfterWorksPrefix(text, i + 1, names, out sessionId, out int skip, out length))
                    continue;
                i = skip + length - 1;
                if (mentioned.Contains(sessionId)) continue;
                mentioned.Add(sessionId);
                continue;
            }

            if (!mentioned.Contains(sessionId)) mentioned.Add(sessionId);
            i += length;
        }

        return mentioned;
    }

    // @ 之后先剥作品名前缀再认名字：前导的「《…》」（及随后的冒号/空格），或无书名号时冒号之前的一段（如「死亡笔记：」）。
    // 冒号回退只看 @ 后 24 字内：再远的冒号多半是正文里的标点，不是点名前缀
    private static bool TryMatchAfterWorksPrefix(string text, int start, GroupSpeakerNames<string> names,
        out string sessionId, out int matchStart, out int length)
    {
        const int maxColonLookahead = 24;
        matchStart = start;
        length = 0;
        sessionId = default!;

        int after = start;
        if (after < text.Length && text[after] == '《')
        {
            int close = text.IndexOf('》', after + 1);
            if (close >= 0)
            {
                after = close + 1;
                while (after < text.Length && (text[after] is ':' or '：' or ' ' or '　')) after++;
            }
        }
        else
        {
            int end = Math.Min(text.Length, start + maxColonLookahead);
            for (int j = start; j < end; j++)
            {
                if (text[j] is not (':' or '：')) continue;
                after = j + 1;
                while (after < text.Length && (text[after] == ' ' || text[after] == '　')) after++;
                break;
            }
        }

        if (after == start || after >= text.Length) return false;
        if (!names.TryMatchStart(text.AsSpan(after), out sessionId, out length)) return false;
        matchStart = after;
        return true;
    }
}
