namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 无限模式心跳文件（ADR 0055 §8）：化身在无限模式里每轮自己用 Write/Edit 把心跳状态行
/// 追加进这一份文件——程序不再剥离 ⟦…⟧ 行、也不代写（见 <c>GroupChatCoordinator.Avatar</c>），
/// 正文只进群。路径与群流水同一间草稿目录（群壳那一间，成员与化身的 $DRAFT 都指它），
/// 留给用户与巡检读——谁在阻碍、卡在哪、试过什么。
/// 产物枚举排除它（见 <c>GroupArtifacts</c>），不参与「本群产物」指纹，免得保险丝与退避永不触发。
/// </summary>
public static class GroupHeartbeatLog
{
    /// <summary>心跳文件固定名（草稿目录里）：提示词与 <see cref="GroupHeartbeatLog"/> 用它指路，产物枚举排除它</summary>
    public const string FileName = "心跳.md";

    /// <summary>
    /// 心跳文件路径：与产物区同一间草稿目录（群壳那一间，成员与化身的 $DRAFT 都指它）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>绝对路径</returns>
    public static string PathOf(ChatSession group) =>
        Path.Combine(GroupArtifacts.DraftRoomOf(group), FileName);
}
