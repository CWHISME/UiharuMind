using System.Collections.Generic;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Artifacts;

/// <summary>单聊会话的产物：草稿目录（子代理也落这一间）加上它自己改过的工作区文件</summary>
/// <param name="session">会话</param>
public sealed class SessionArtifactSource(ChatSession session) : IArtifactSource
{
    /// <inheritdoc />
    public string DraftRoom { get; } = AgentBuildProfile.DraftRoomOf(session);

    /// <inheritdoc />
    public string Title => Loc.Text(LangKey.SessionArtifactsTitle);

    /// <inheritdoc />
    public string Hint => Loc.Text(LangKey.SessionArtifactsHint);

    /// <inheritdoc />
    public IReadOnlyList<GroupArtifact> Collect() => GroupArtifacts.CollectForSession(session);

    /// <inheritdoc />
    public bool IsRelevant(string sessionId) => sessionId == session.SessionId;
}
