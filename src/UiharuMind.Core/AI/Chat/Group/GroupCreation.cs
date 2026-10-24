using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 按「群名 + 成员的名字」建群的那一条共用路径：把点名的成员解析成角色、对齐模型、定主持人，
/// 最后交给 <see cref="GroupChatSessions.Create"/> 落盘。
///
/// 工具（<c>chatsession.create-group</c>）与开发脚本（<c>group.create</c>）都从这里走——
/// 两边各写一遍时，成员解析的口径、模型与成员的下标对齐、校验的措辞迟早对不上。
/// 用户手建的群不经过它（那个流程在弹窗里挑角色卡，已经拿到 <see cref="CharacterData"/> 了）。
/// </summary>
public static class GroupCreation
{
    /// <summary>
    /// 建一个群并落盘
    /// </summary>
    /// <param name="spec">建群参数</param>
    /// <returns>群壳会话</returns>
    /// <exception cref="ArgumentException">群名为空、一个成员都没点、点名了不认识的角色、主持人不在成员里、模型列表对不上</exception>
    public static ChatSession Create(GroupCreationSpec spec)
    {
        string name = spec.Name.Trim();
        if (name.Length == 0) throw new ArgumentException("群名不能为空。");

        List<CharacterData> members = ResolveMembers(spec.Members);
        // 单人成群在这条路上是合法的（开发脚本用它跑「群路径」对照）：用户那边的「至少两位」
        // 是建群弹窗的产品规则，由工具自己钉，不在这里替所有调用方做主
        if (members.Count == 0) throw new ArgumentException("一个群至少要有一位成员。");

        List<string?>? modelNames = AlignModels(spec.ModelNames, members.Count);
        GroupSchedule schedule = new(spec.Mode, spec.StopPolicy, HostIndexOf(spec.Host, members));

        return GroupChatSessions.Create(name, spec.IsAgentGroup, members, spec.WorkspacePath, modelNames, schedule);
    }

    // 成员按给出的顺序解析，顺序即发言顺序。同一张卡点两次是调用方写错了，直接报出来——
    // 静默去重会让「第二个人」凭空消失，模型还以为群建成了
    private static List<CharacterData> ResolveMembers(IReadOnlyList<string> keys)
    {
        List<CharacterData> members = [];
        foreach (string key in keys.Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            CharacterData character = CharacterManager.Instance.Find(key)
                                      ?? throw new ArgumentException($"角色库里没有「{key}」这个角色。");
            if (!GroupChatSessions.CanJoin(character))
            {
                throw new ArgumentException($"「{character.CharacterName}」不能进群——用户卡是「我是谁」，不开会话。");
            }

            if (members.Exists(x => x.CharacterId == character.CharacterId))
            {
                throw new ArgumentException($"「{character.CharacterName}」被点了两次；同一位角色在一个群里只有一份会话。");
            }

            members.Add(character);
        }

        return members;
    }

    // 模型名单与成员一一对应；空串与缺位都表示「这位跟随全局」。
    // 多出来的一定是写错了（第 N 个模型会静默落到不存在的成员身上），当场报
    private static List<string?>? AlignModels(IReadOnlyList<string>? names, int memberCount)
    {
        if (names == null || names.Count == 0) return null;
        if (names.Count > memberCount)
        {
            throw new ArgumentException($"models 给多了：{names.Count} 个模型对应 {memberCount} 位成员。");
        }

        List<string?> aligned = new(memberCount);
        for (int i = 0; i < memberCount; i++)
        {
            string? model = i < names.Count ? names[i].Trim() : string.Empty;
            aligned.Add(string.IsNullOrEmpty(model) ? null : model);
        }

        return aligned;
    }

    // 主持人按名字点：-1 为无主持人。不在成员里时按错误处理——
    // 静默退成「无主持人」会让群里没有人收尾，而调用方以为设上了
    private static int HostIndexOf(string? host, IReadOnlyList<CharacterData> members)
    {
        if (string.IsNullOrWhiteSpace(host)) return -1;

        string key = host.Trim();
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].CharacterId == key || members[i].CharacterName == key) return i;
        }

        throw new ArgumentException($"主持人必须是成员之一：「{key}」不在成员里。");
    }
}

/// <summary>
/// 建群参数：名字用字符，成员/主持人/模型用角色标识或显示名——调用方不必先自己解析角色卡
/// </summary>
public sealed record GroupCreationSpec
{
    /// <summary>群名</summary>
    public required string Name { get; init; }

    /// <summary>成员的角色标识或显示名，顺序即发言顺序</summary>
    public required IReadOnlyList<string> Members { get; init; }

    /// <summary>成员各自的模型名，顺序与 <see cref="Members"/> 一致；null 或空串 = 跟随全局</summary>
    public IReadOnlyList<string>? ModelNames { get; init; }

    /// <summary>是不是智能体群；智能体群才绑 <see cref="WorkspacePath"/></summary>
    public bool IsAgentGroup { get; init; } = true;

    /// <summary>智能体群的工作区；普通群忽略</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>调度模式</summary>
    public EGroupScheduleMode Mode { get; init; } = EGroupScheduleMode.Serial;

    /// <summary>并行群聊的停止条件</summary>
    public EGroupStopPolicy StopPolicy { get; init; } = EGroupStopPolicy.Conservative;

    /// <summary>主持人的角色标识或显示名；null 为无主持人</summary>
    public string? Host { get; init; }
}
