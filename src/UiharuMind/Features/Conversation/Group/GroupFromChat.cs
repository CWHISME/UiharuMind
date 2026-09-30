using System.Threading.Tasks;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 从单聊开群（方案 v6 §6.6b 规则 5 的轻量落地）：另开一个群，原单聊原样不动，不做就地升级。
/// 建群弹窗预先勾上原单聊的角色；确认后先让原单聊的模型写一份背景摘要，连同草稿一起建群——
/// 草稿只在装载会话时读一次，先写后建才不会跟用户在新群里打字抢
/// </summary>
internal static class GroupFromChat
{
    /// <summary>
    /// 能不能从这个会话开群：普通的单聊才行（群壳、群成员会话、子会话都不算）
    /// </summary>
    /// <param name="meta">会话元数据</param>
    /// <returns>能为 true</returns>
    public static bool CanStartFrom(ChatSessionMeta? meta) =>
        meta is { IsGroup: false, IsGroupMember: false, IsSubSession: false };

    /// <summary>
    /// 走一遍：弹窗 → 写背景 → 建群
    /// </summary>
    /// <param name="source">原单聊</param>
    /// <param name="workspacePath">原单聊此刻的工作区（智能体群绑它）；普通对话为 null</param>
    /// <param name="messages">写背景期间的提示用</param>
    /// <returns>建好的群；用户取消为 null</returns>
    public static async Task<ChatSession?> CreateAsync(ChatSession source, string? workspacePath, IMessageService messages)
    {
        // 群的类型跟原单聊那一侧，与空态建群同一口径：agent 卡开成的普通对话落在普通群（ADR 0050）
        bool isAgentGroup = SessionManager.IsAgentSide(source.ToMeta());
        string? workspace = isAgentGroup ? workspacePath : null;
        string characterName = source.CharacterData.CharacterName;
        GroupCreateRequest? request = await GroupCreateWindow.ShowAsync(isAgentGroup, workspace,
            [source.CharacterData], source.Title);
        if (request == null) return null;

        string? summary = null;
        if (source.History.Count > 0)
        {
            messages.ShowNotification(Loc.Text(LangKey.GroupFromChatSummarizing, characterName));
            summary = await GroupBackground.WriteAsync(source);
            if (summary == null)
            {
                messages.ShowNotification(Loc.Text(LangKey.GroupFromChatSummaryFailed),
                    severity: MessageSeverity.Warning);
            }
        }

        ChatSession group = GroupChatSessions.Create(request.Name, isAgentGroup, request.Members, workspace,
            request.MemberModelNames, request.Schedule);
        if (summary != null)
        {
            group.ComposerDraft = GroupBackground.Compose(characterName, summary);
            group.SaveMeta(touchUpdatedAt: false);
        }

        return group;
    }
}
