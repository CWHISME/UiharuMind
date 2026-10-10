using System.Collections.Generic;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Features.Conversation.Artifacts;

/// <summary>产物区盯的是谁：群（全群共用一间草稿目录）或单聊会话。刷新、监听与列表同步由 <see cref="ArtifactsViewData"/> 统一做</summary>
public interface IArtifactSource
{
    /// <summary>草稿目录绝对路径</summary>
    string DraftRoom { get; }

    /// <summary>面板标题</summary>
    string Title { get; }

    /// <summary>面板说明</summary>
    string Hint { get; }

    /// <summary>
    /// 收集产物（后台线程上调）
    /// </summary>
    /// <returns>产物，最近改过的在前</returns>
    IReadOnlyList<GroupArtifact> Collect();

    /// <summary>
    /// 这个会话跑完一次调用或一轮后要不要刷新
    /// </summary>
    /// <param name="sessionId">会话编号</param>
    /// <returns>是本来源的会话</returns>
    bool IsRelevant(string sessionId);
}
