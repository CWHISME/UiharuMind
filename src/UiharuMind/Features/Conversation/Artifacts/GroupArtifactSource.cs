using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Artifacts;

/// <summary>本群产物：草稿目录加上成员改过的工作区文件</summary>
/// <param name="group">群壳会话</param>
public sealed class GroupArtifactSource(ChatSession group) : IArtifactSource
{
    /// <inheritdoc />
    public string DraftRoom { get; } = GroupArtifacts.DraftRoomOf(group);

    /// <inheritdoc />
    public string Title => Loc.Text(LangKey.GroupArtifactsTitle);

    /// <inheritdoc />
    public string Hint => Loc.Text(LangKey.GroupArtifactsHint);

    /// <inheritdoc />
    public IReadOnlyList<GroupArtifact> Collect() => GroupArtifacts.CollectFor(group);

    /// <inheritdoc />
    public bool IsRelevant(string sessionId) => group.GroupMemberSessionIds.Contains(sessionId);
}
