using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Extensions;

namespace UiharuMind.Core.Tests.Execution.Extensions;

/// <summary>
/// 群壳上勾的「扩展不再询问」要落到成员身上，且<b>不依赖群壳本体在内存里</b>：
/// 应用刚启动、或群壳被冷历史卸载请出内存时，名单从索引那一份读——
/// 读不到就等于「没勾」，成员每个扩展调用都要用户点一次（ADR 0079）
/// </summary>
public class GroupExtensionApprovalInheritanceTests
{
    [Fact]
    public void GroupStar_ReachesMember_EvenWhenTheGroupIsNotLoaded()
    {
        CharacterData character = new() { CharacterId = "scratch", CharacterName = "临时" };
        ChatSession group = SessionManager.Instance.StartNewSession(character);
        group.SetSessionApprovedExtension(ExtensionCalls.AllExtensions, true);
        ChatSession member = SessionManager.Instance.StartNewSession(character);
        member.GroupId = group.SessionId;

        SessionManager.Instance.Release(group.SessionId);
        Assert.Null(SessionManager.Instance.GetLoaded(group.SessionId));
        Assert.NotNull(SessionManager.Instance.GetMeta(group.SessionId));

        AgentBuildProfile profile = AgentBuildProfile.FromSession(member);
        IReadOnlyList<string>? inherited = profile.SessionExtensionApprovalSource!.Invoke();

        Assert.NotNull(inherited);
        Assert.Contains(ExtensionCalls.AllExtensions, inherited!);
    }

    /// <summary>定时任务派出去的活同样没人看着：子会话跟着派活者算「无人在场」</summary>
    [Fact]
    public void SubSessionOfAScheduledRun_IsUnattended()
    {
        CharacterData character = new() { CharacterId = "scratch", CharacterName = "临时" };
        ChatSession parent = SessionManager.Instance.StartNewSession(character);
        parent.IsScheduledRun = true;
        ChatSession sub = SessionManager.Instance.StartNewSession(character);
        sub.ParentSessionId = parent.SessionId;

        Assert.True(AgentBuildProfile.FromSession(sub).UnattendedSource?.Invoke() == true);
    }

    /// <summary>有人在场的普通会话不算：本机服务与界外本地文件仍按用户勾的那一格走</summary>
    [Fact]
    public void OrdinarySession_IsAttended()
    {
        ChatSession session = SessionManager.Instance.StartNewSession(
            new CharacterData { CharacterId = "scratch-plain", CharacterName = "临时" });

        Assert.False(AgentBuildProfile.FromSession(session).UnattendedSource?.Invoke() == true);
    }

    /// <summary>离席探针接上后，规则在运行期现问它：离席那一刻「只认扩展不再询问」那一档照拒</summary>
    [Fact]
    public void AwayProbe_MakesTheSessionUnattended()
    {
        ChatSession session = SessionManager.Instance.StartNewSession(
            new CharacterData { CharacterId = "scratch-away", CharacterName = "临时" });
        Func<bool>? source = AgentBuildProfile.FromSession(session).UnattendedSource;

        Assert.False(source?.Invoke() == true);
        try
        {
            AgentBuildProfile.AwayProbe = _ => true;
            // 现取：同一份来源在探针接上之后立刻回答「无人在场」
            Assert.True(source?.Invoke() == true);
        }
        finally
        {
            AgentBuildProfile.AwayProbe = null;
        }
    }

    /// <summary>界面上的状态灯看「事实上生效」的那份：群成员自己名单是空的，但群壳的 `*` 算数</summary>
    [Fact]
    public void EffectiveApprovals_IncludeTheGroupsStar()
    {
        CharacterData character = new() { CharacterId = "scratch", CharacterName = "临时" };
        ChatSession group = SessionManager.Instance.StartNewSession(character);
        group.SetSessionApprovedExtension(ExtensionCalls.AllExtensions, true);
        ChatSession member = SessionManager.Instance.StartNewSession(character);
        member.GroupId = group.SessionId;

        Assert.Empty(member.SnapshotSessionApprovedExtensions());
        Assert.Contains(ExtensionCalls.AllExtensions, AgentBuildProfile.EffectiveApprovalsOf(member));
    }
}
