using System.Text.Json;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话形态（ADR 0050）：同一张 agent 卡可开成普通对话会话——不装工具、不绑工作区、只借人格。
/// 这组测试钉住：形态的存储与默认、老数据定格、列表归类、单向锁计数、以及群的收人放开。
/// </summary>
public class SessionFormTests
{
    public SessionFormTests()
    {
        // 枚举路径的角色（ChenXiAgent / None / UserCard）经 DefaultCharacterManager 解析；
        // 内容卡要 CharacterManager 装库，这里刻意不用——显式形态的归类不查角色库
        DefaultCharacterManager.Instance.OnInitialize();
    }

    //================= 存储与默认 =================

    [Fact]
    public void SessionConstructor_DefaultsFormToIdentity()
    {
        CharacterData agent = new() { IsAgent = true };
        CharacterData chat = new() { IsAgent = false };

        Assert.True(new ChatSession("t", agent).IsAgentForm);
        Assert.False(new ChatSession("t", chat).IsAgentForm);
    }

    [Fact]
    public void EffectiveIsAgentForm_FallsBackToIdentity_WhenNotFrozen()
    {
        ChatSession session = new() { CharacterId = nameof(DefaultCharacter.ChenXiAgent) }; //null = 未定格

        Assert.Null(session.IsAgentForm);
        Assert.True(session.EffectiveIsAgentForm); //跟身份 → agent

        session.IsAgentForm = false; //agent 卡开成普通对话
        Assert.False(session.EffectiveIsAgentForm);
    }

    [Fact]
    public void Serialization_RoundTrips_ExplicitForm_AndOldArchiveReadsNull()
    {
        ChatSession session = new() { CharacterId = "BaiLuAgent", IsAgentForm = false };
        string json = JsonSerializer.Serialize(session, SessionJsonOptions.Default);

        Assert.Contains("\"isAgentForm\"", json);
        ChatSession restored = JsonSerializer.Deserialize<ChatSession>(json, SessionJsonOptions.Default)!;
        Assert.False(restored.IsAgentForm);
        Assert.False(restored.EffectiveIsAgentForm);

        // 老存档没有这个字段 → 读回 null（未定格），由装载路径按身份补写
        ChatSession old = JsonSerializer.Deserialize<ChatSession>(
            """{"SessionId":"s1","Title":"t","CharacterId":"BaiLuAgent"}""",
            SessionJsonOptions.Default)!;
        Assert.Null(old.IsAgentForm);
    }

    [Fact]
    public void ToMeta_CarriesSessionForm()
    {
        ChatSession session = new() { IsAgentForm = false };

        Assert.False(session.ToMeta().IsAgentForm);
    }

    //================= 老数据定格 =================

    [Fact]
    public void LoadingOldSession_FreezesFormFromCurrentIdentity()
    {
        // 老存档没有形态字段（null）；角色是智能体 → 装载时按当前身份定格为 agent 形态
        ChatSession old = new() { CharacterId = nameof(DefaultCharacter.ChenXiAgent) };
        Assert.Null(old.IsAgentForm);
        SessionManager.Instance.Add(old);
        SessionManager.Instance.Release(old.SessionId); //摘出缓存,强制走读盘 + 定格
        try
        {
            ChatSession? loaded = SessionManager.Instance.Load(old.SessionId);

            Assert.NotNull(loaded);
            Assert.True(loaded.IsAgentForm); //已定格：此后翻身份不再挪它
        }
        finally
        {
            SessionManager.Instance.Delete(old.SessionId);
        }
    }

    //================= 列表归类 =================

    [Fact]
    public void AgentCard_ChatFormSession_GoesToChatSide()
    {
        ChatSessionMeta meta = new() { CharacterId = "BaiLuAgent", IsAgentForm = false };

        Assert.False(SessionManager.IsAgentSide(meta));
        Assert.True(SessionManager.IsChatSide(meta));
    }

    [Fact]
    public void AgentCard_AgentFormSession_GoesToAgentSide()
    {
        ChatSessionMeta meta = new() { CharacterId = "BaiLuAgent", IsAgentForm = true };

        Assert.True(SessionManager.IsAgentSide(meta));
        Assert.False(SessionManager.IsChatSide(meta));
    }

    [Fact]
    public void Routing_NullForm_FallsBackToIdentity()
    {
        Assert.True(SessionManager.IsAgentSide(new ChatSessionMeta { CharacterId = nameof(DefaultCharacter.ChenXiAgent) }));
        Assert.True(SessionManager.IsChatSide(new ChatSessionMeta { CharacterId = nameof(DefaultCharacter.None) }));
    }

    [Fact]
    public void UserCard_IsOnNeitherSide()
    {
        ChatSessionMeta meta = new() { CharacterId = nameof(DefaultCharacter.UserCard), IsAgentForm = false };

        Assert.False(SessionManager.IsAgentSide(meta));
        Assert.False(SessionManager.IsChatSide(meta));
    }

    [Fact]
    public void GroupShell_SideComesFromGroupType()
    {
        Assert.True(SessionManager.IsAgentSide(new ChatSessionMeta { IsGroup = true, IsAgentGroup = true }));
        Assert.True(SessionManager.IsChatSide(new ChatSessionMeta { IsGroup = true, IsAgentGroup = false }));
    }

    //================= 单向锁计数（ADR 0050 决策 5） =================

    [Fact]
    public void CountSessionsOf_CountsAgentFormSessionsOnly()
    {
        string charId = "form-count-" + Guid.NewGuid().ToString("N");
        SessionManager.Instance.Add(new ChatSession { CharacterId = charId, IsAgentForm = true });
        SessionManager.Instance.Add(new ChatSession { CharacterId = charId, IsAgentForm = false });
        try
        {
            // 纯聊会话不算：白露名下全是普通对话时仍可翻回普通角色
            Assert.Equal(1, SessionManager.Instance.CountSessionsOf(charId));
        }
        finally
        {
            foreach (ChatSessionMeta meta in SessionManager.Instance.GetSessions()
                         .Where(x => x.CharacterId == charId).ToList())
            {
                SessionManager.Instance.Delete(meta.SessionId);
            }
        }
    }

    //================= 群（ADR 0050 决策 3） =================

    [Fact]
    public void CanJoin_AcceptsAgentCardsInChatGroups()
    {
        CharacterData agent = new() { IsAgent = true };
        CharacterData userCard = new() { IsUserCard = true };

        Assert.True(GroupChatSessions.CanJoin(agent)); //普通群也收；群类型不影响能不能进（ADR 0050）
        Assert.False(GroupChatSessions.CanJoin(userCard)); //用户卡哪儿都不收
    }

    [Fact]
    public void GroupCreate_SetsMemberFormByGroupTypeTimesIdentity()
    {
        CharacterData agent = new() { IsAgent = true, CharacterName = "A" };
        CharacterData chat = new() { IsAgent = false, CharacterName = "C" };

        ChatSession agentGroup = GroupChatSessions.Create("g-agent", true, [agent, chat], "/ws", null);
        try
        {
            List<ChatSessionMeta> members = SessionManager.Instance.GetGroupMembers(agentGroup.SessionId);
            Assert.True(members.Single(x => x.CharacterId == agent.CharacterId).IsAgentForm);
            Assert.False(members.Single(x => x.CharacterId == chat.CharacterId).IsAgentForm);
        }
        finally
        {
            SessionManager.Instance.Delete(agentGroup.SessionId);
        }

        ChatSession chatGroup = GroupChatSessions.Create("g-chat", false, [agent], null, null);
        try
        {
            ChatSessionMeta member = Assert.Single(SessionManager.Instance.GetGroupMembers(chatGroup.SessionId));
            Assert.False(member.IsAgentForm); //agent 卡进普通群 = chat 形态
            Assert.False(SessionManager.IsAgentSide(member));
        }
        finally
        {
            SessionManager.Instance.Delete(chatGroup.SessionId);
        }
    }
}
