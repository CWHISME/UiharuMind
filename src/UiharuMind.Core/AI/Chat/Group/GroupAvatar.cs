using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群的化身会话（ADR 0055）：每群至多一份，第一次离席时建，之后跨离席保留，随群级联删除。
/// 身份载体是内部智能体卡 <see cref="DefaultCharacter.GroupAvatarAgent"/>：卡上是化身的规矩，「用户是谁」靠注入用户卡
/// （用户卡本身开不了会话、也没有工具配置）。
/// 挂 <see cref="ChatSession.GroupId"/> 但不进名单——在场名单、@ 与唤醒都看不到它
/// </summary>
public static class GroupAvatar
{
    /// <summary>新建时补给它的群发言条数：老群的流水可能很长，只交最近这一段，免得第一份投递就撑爆上下文</summary>
    public const int InitialBacklog = 30;

    /// <summary>
    /// 群的化身会话的元数据
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>化身；还没建为 null</returns>
    public static ChatSessionMeta? MetaOf(string groupId) =>
        SessionManager.Instance.GetGroupMembers(groupId).FirstOrDefault(x => x.IsGroupAvatar);

    /// <summary>
    /// 取群的化身会话，没有就建一份并落盘；每次离席开始时调用，顺带换上这次选的模型、游标至多补最近一段
    /// </summary>
    /// <param name="group">群壳会话（须是智能体群）</param>
    /// <param name="modelName">这次离席化身用的模型名；null 跟随全局</param>
    /// <returns>化身会话</returns>
    /// <exception cref="ArgumentException">不是智能体群</exception>
    public static ChatSession EnsureFor(ChatSession group, string? modelName)
    {
        if (group is not { IsGroup: true, IsAgentGroup: true })
        {
            throw new ArgumentException("Only agent groups have an avatar.", nameof(group));
        }

        if (MetaOf(group.SessionId) is { } meta && SessionManager.Instance.Load(meta.SessionId) is { } existing)
        {
            // 上次离席之后用户自己带群跑了多久都有可能：同样只补最近这一段
            existing.SessionModelName = modelName;
            existing.GroupCursor = Math.Max(existing.GroupCursor, BacklogCursor(group));
            existing.SaveMeta(touchUpdatedAt: false);
            return existing;
        }

        ChatSession avatar = New(group, modelName);
        SessionManager.Instance.Add(avatar);
        return avatar;
    }

    /// <summary>
    /// 为群建一份化身会话（不入库）。游标停在最近 <see cref="InitialBacklog"/> 条群发言之前
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="modelName">模型名；null 跟随全局</param>
    /// <returns>化身会话</returns>
    public static ChatSession New(ChatSession group, string? modelName) =>
        // 不走带角色的构造：那会写入开场白
        new()
        {
            CharacterId = nameof(DefaultCharacter.GroupAvatarAgent),
            Title = $"{group.Title} · {CharacterManager.Instance.UserCharacterName}",
            Description = group.Title,
            GroupId = group.SessionId,
            IsGroupAvatar = true,
            IsAgentForm = true,
            SessionModelName = modelName,
            GroupCursor = BacklogCursor(group),
        };

    private static int BacklogCursor(ChatSession group) => Math.Max(0, group.History.Count - InitialBacklog);
}
