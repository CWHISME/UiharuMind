using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 从群聊另开一个群（会话页顶栏「从这里建群」按钮的群聊分支）：参数照抄当前群——成员及发言顺序、
/// 各自的模型、调度与主持人、群类型与工作区、权限档；历史与离席化身不带。
/// 建群弹窗预填后可再改，原群不动。
/// </summary>
internal static class GroupFromGroup
{
    /// <summary>
    /// 能不能从这个会话开群：群壳才行（单聊走 <see cref="GroupFromChat"/>）
    /// </summary>
    /// <param name="meta">会话元数据</param>
    /// <returns>能为 true</returns>
    public static bool CanStartFrom(ChatSessionMeta? meta) => meta is { IsGroup: true };

    /// <summary>
    /// 走一遍：取群参数 → 建群弹窗（预填）→ 建群
    /// </summary>
    /// <param name="source">群壳会话</param>
    /// <returns>新群；取消为 null</returns>
    public static async Task<ChatSession?> CreateAsync(ChatSession source)
    {
        if (!source.IsGroup) return null;

        // 名单即发言顺序；成员的模型名各自钉着（跟随全局为 null）
        GroupRoster roster = GroupRoster.Of(source);
        List<CharacterData> members = roster.Present.Select(x => x.Character).ToList();
        List<string?> memberModelNames = roster.Present.Select(x => x.Meta.SessionModelName).ToList();

        // 主持人按发言顺序记，建群请求里同样按下标取；不在名单（不存在）为 -1
        int hostIndex = source.GroupHostSessionId is { } host
            ? source.GroupMemberSessionIds.ToList().IndexOf(host)
            : -1;
        GroupSchedule schedule = new(source.GroupScheduleMode, source.GroupStopPolicy, hostIndex);

        GroupCreateRequest? request = await GroupCreateWindow.ShowAsync(source.IsAgentGroup,
            source.WorkspacePath, members, source.Title, memberModelNames, schedule);
        if (request == null) return null;

        // 权限档照抄原群：与「新会话默认档」无关，它是有意为之的群配置
        ChatSession group = GroupChatSessions.Create(request.Name, source.IsAgentGroup, request.Members,
            source.WorkspacePath, request.MemberModelNames, request.Schedule, source.PermissionModeIndex);
        return group;
    }
}