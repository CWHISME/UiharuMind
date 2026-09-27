using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// <c>@</c> 补全（ADR 0049 决策 5）：群里光标前是 <c>@半个名字</c> 时给成员候选，采纳即换成「@名字 」。
/// 不是群就没有成员，自然不弹
/// </summary>
public sealed class MentionCompletionSource : ICompletionSource
{
    private const int MaxQueryLength = 20; //@ 之后往回找这么远还没碰到空白，就不当点名

    private readonly Func<IEnumerable<MentionTarget>> _members;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="members">当前群的成员；不是群为空</param>
    public MentionCompletionSource(Func<IEnumerable<MentionTarget>> members)
    {
        _members = members;
    }

    public bool TryMatch(string text, int caret, out CompletionMatch match)
    {
        match = default;
        if (!_members().Any()) return false;
        return TryFindMention(text, caret, out match);
    }

    public Task<IReadOnlyList<CompletionCandidate>> GetCandidatesAsync(string query)
    {
        List<MentionTarget> members = _members().ToList();
        // 前缀命中排前面，名字中间命中的补在后面（中文名常只记得后半截）
        IEnumerable<MentionTarget> ordered = members
            .Where(x => x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Concat(members.Where(x => !x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                                       && x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
        IReadOnlyList<CompletionCandidate> candidates = ordered
            .Select(x => new CompletionCandidate
            {
                Label = "@" + x.Name,
                Insertion = $"@{x.Name} ",
                Description = x.Description,
                Icon = x.Icon,
            })
            .ToList();
        return Task.FromResult(candidates);
    }

    public void Reset()
    {
    }

    /// <summary>
    /// 光标前是不是一个没写完的点名：往回找到 <c>@</c>（或全角 ＠）之前没碰到空白。
    /// @ 紧跟在字母数字后面的不算（<c>a@b.com</c>）
    /// </summary>
    /// <param name="text">输入框内容</param>
    /// <param name="caret">光标位置</param>
    /// <param name="match">从 @ 到光标的范围，查询词是 @ 之后那段</param>
    /// <returns>是为 true</returns>
    public static bool TryFindMention(string text, int caret, out CompletionMatch match)
    {
        match = default;
        caret = Math.Clamp(caret, 0, text.Length);
        for (int i = caret - 1; i >= 0 && caret - i <= MaxQueryLength + 1; i--)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) return false;
            if (c != '@' && c != '＠') continue;
            if (i > 0 && char.IsAsciiLetterOrDigit(text[i - 1])) return false;

            match = new CompletionMatch(i, caret, text[(i + 1)..caret]);
            return true;
        }

        return false;
    }
}

/// <summary>可以被 @ 的一位成员</summary>
/// <param name="Name">名字</param>
/// <param name="Description">一句描述</param>
/// <param name="Icon">头像</param>
public sealed record MentionTarget(string Name, string Description, Bitmap? Icon);
