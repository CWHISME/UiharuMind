using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 成员的工作区与权限档跟群走、不存副本：装配现取群的那一份。
/// 各存一份时，群看着是自动编辑，某位成员却在完全自动档下跑 shell；群换了工作区，成员窗口还指着旧目录
/// </summary>
public class GroupMemberSettingsTests
{
    [Fact]
    public void MemberAssembly_ReadsTheGroupsWorkspaceAndPermission_Live()
    {
        CharacterData agent = new() { CharacterId = nameof(DefaultCharacter.ChenXiAgent), CharacterName = "A", IsAgent = true };
        CharacterData chat = new() { CharacterName = "B" };
        ChatSession group = GroupChatSessions.Create("g-settings", true, [agent, chat], "/ws/one");
        try
        {
            List<ChatSession> members = group.GroupMemberSessionIds.Select(id => SessionManager.Instance.Load(id)!).ToList();
            ChatSession agentMember = members[0];
            ChatSession chatMember = members[1];
            agentMember.WorkspacePath = "/stale";
            agentMember.PermissionModeIndex = 2;
            group.PermissionModeIndex = 0;
            group.SaveMeta(touchUpdatedAt: false);

            AgentBuildProfile profile = AgentBuildProfile.FromSession(agentMember);
            Assert.Equal("/ws/one", profile.WorkspacePath);
            Assert.Equal(EAgentPermissionMode.ReadOnly, profile.PermissionMode);
            Assert.Null(AgentBuildProfile.FromSession(chatMember).WorkspacePath); //chat 形态不绑

            // 群改了：不推给谁，装配好的那份下一条调用就按新档审批
            group.WorkspacePath = "/ws/two";
            group.PermissionModeIndex = 1;
            group.SaveMeta(touchUpdatedAt: false);
            Assert.Equal(EAgentPermissionMode.AutoEdit, profile.PermissionModeSource!());
            Assert.Equal("/ws/two", GroupChatSessions.WorkspaceOf(agentMember));
            Assert.Equal("/ws/two", GroupChatSessions.WorkspaceOf(SessionManager.Instance.GetMeta(agentMember.SessionId)!));
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }
    }
}
