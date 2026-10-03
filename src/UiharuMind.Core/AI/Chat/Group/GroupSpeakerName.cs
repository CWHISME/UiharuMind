namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群里一个人的名字，以及别人（或他自己）会怎么叫他。@ 点名、剥自加前缀都按这一份认人，不各写一套。
///
/// 名字末尾可带括号别名，如「五更琉璃（黑猫）」：全名、括号前的本名、括号里的别名都是他；
/// 本名或别名中连续的两字起也算（原作角色常拿自称、昵称叫人：琉璃、黑子、千空）。
/// 部分叫法会与别人撞，所以「是不是他」要在整份名单里判，见 <see cref="GroupSpeakerNames"/>
/// </summary>
public sealed class GroupSpeakerName
{
    private const int MinPartialLength = 2; //一个字太容易撞上正文里的字

    /// <summary>
    /// 从显示名拆出各种叫法
    /// </summary>
    /// <param name="displayName">显示名</param>
    public GroupSpeakerName(string displayName)
    {
        Full = displayName.Trim();
        (Own, Alias) = SplitAlias(Full);
    }

    /// <summary>显示名（全名）</summary>
    public string Full { get; }

    /// <summary>括号前的本名；没有括号时就是全名</summary>
    public string Own { get; }

    /// <summary>括号里的别名；没有为 null</summary>
    public string? Alias { get; }

    /// <summary>完整的叫法：全名、本名、别名（去重）</summary>
    public IEnumerable<string> ExactNames => new[] { Full, Own, Alias }.OfType<string>().Where(x => x.Length > 0).Distinct();

    /// <summary>
    /// 这个叫法是不是完整地叫到他（全名、本名或别名）。区分大小写：剥前缀只认他自己的写法，见 <see cref="GroupSpeakerNames{TKey}"/> 的点名才不分
    /// </summary>
    /// <param name="token">叫法</param>
    /// <returns>是为 true</returns>
    public bool IsExactly(string token) => ExactNames.Contains(token.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// 这个叫法是不是叫到他：完整叫法，或本名、别名中连续的两字起。区分大小写
    /// </summary>
    /// <param name="token">叫法</param>
    /// <returns>是为 true</returns>
    public bool IsCalledBy(string token)
    {
        string value = token.Trim();
        if (IsExactly(value)) return true;
        return value.Length >= MinPartialLength
               && new[] { Own, Alias }.OfType<string>().Any(x => x.Contains(value, StringComparison.Ordinal));
    }

    /// <summary>
    /// 所有能叫到他的写法：完整叫法，加本名、别名中连续的两字起的各段
    /// </summary>
    /// <returns>叫法，不去重</returns>
    public IEnumerable<string> AllCalls()
    {
        foreach (string name in ExactNames) yield return name;
        foreach (string source in new[] { Own, Alias }.OfType<string>())
        {
            for (int start = 0; start < source.Length; start++)
            for (int length = MinPartialLength; start + length <= source.Length; length++)
                yield return source.Substring(start, length);
        }
    }

    // 「本名（别名）」拆开；括号全角半角都认，没有括号就只有本名
    private static (string Own, string? Alias) SplitAlias(string full)
    {
        int open = full.LastIndexOfAny(['（', '(']);
        if (open <= 0 || full[^1] is not ('）' or ')')) return (full, null);

        string own = full[..open].Trim();
        string alias = full[(open + 1)..^1].Trim();
        return own.Length == 0 ? (full, null) : (own, alias.Length > 0 ? alias : null);
    }
}

/// <summary>
/// 一份名单上各人的叫法，判「这个叫法指的是谁」。完整叫法优先；部分叫法只有恰好指向一个人时才算——两人共用的不算点到谁
/// </summary>
/// <typeparam name="TKey">人的标识（如会话标识）</typeparam>
public sealed class GroupSpeakerNames<TKey> where TKey : notnull
{
    private readonly List<(string Call, TKey Key)> _byLength; //可用的叫法，长的在前

    /// <summary>
    /// 建名单
    /// </summary>
    /// <param name="people">标识与显示名</param>
    public GroupSpeakerNames(IEnumerable<(TKey Key, string DisplayName)> people)
    {
        List<(TKey Key, GroupSpeakerName Name)> named = people
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .Select(x => (x.Key, new GroupSpeakerName(x.DisplayName)))
            .ToList();

        Dictionary<string, TKey> calls = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ambiguous = new(StringComparer.OrdinalIgnoreCase);
        // 完整叫法先占：「白井」是一个人的全名时，不因为也是「白井黑子」的一段就作废
        Claim(named.SelectMany(x => x.Name.ExactNames.Select(call => (call, x.Key))));
        HashSet<string> exact = new(calls.Keys.Concat(ambiguous), StringComparer.OrdinalIgnoreCase);
        Claim(named.SelectMany(x => x.Name.AllCalls().Where(call => !exact.Contains(call)).Select(call => (call, x.Key))));

        _byLength = calls.Where(x => !ambiguous.Contains(x.Key))
            .Select(x => (x.Key, x.Value))
            .OrderByDescending(x => x.Key.Length)
            .ToList();
        return;

        void Claim(IEnumerable<(string Call, TKey Key)> candidates)
        {
            foreach ((string call, TKey key) in candidates)
            {
                if (ambiguous.Contains(call)) continue;
                if (!calls.TryGetValue(call, out TKey? owner)) calls[call] = key;
                else if (!EqualityComparer<TKey>.Default.Equals(owner, key))
                {
                    calls.Remove(call);
                    ambiguous.Add(call);
                }
            }
        }
    }

    /// <summary>
    /// 正文这一处开头叫到的是谁：取最长的那个叫法（中文名后面不带空格，只能按开头认）
    /// </summary>
    /// <param name="text">从叫法开始的正文</param>
    /// <param name="key">叫到的人</param>
    /// <param name="length">叫法的长度</param>
    /// <returns>叫到了人为 true</returns>
    public bool TryMatchStart(ReadOnlySpan<char> text, out TKey key, out int length)
    {
        foreach ((string call, TKey owner) in _byLength)
        {
            if (!text.StartsWith(call.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
            key = owner;
            length = call.Length;
            return true;
        }

        key = default!;
        length = 0;
        return false;
    }
}
