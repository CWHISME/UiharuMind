using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// chatsession.create-group 与 SendMessage 的群分支：建群走 <see cref="GroupCreation"/>（与开发脚本共用），
/// 发群按群名/群壳短号在全库认。用全局 <see cref="SessionManager"/> 跑——建群那条路在 Core 里
/// 就是走全局单例的（工具不注入管理器），所以清理只能自己按标识删干净。
/// 与 <see cref="ChatSessionToolsTests"/> 串行：两边都往共享会话索引写
/// </summary>
[Collection("ChatSessionState")]
public sealed class ChatSessionCreateGroupTests : IDisposable
{
    private static readonly string[] CardIds = ["cg-test-alice", "cg-test-bob"];

    private readonly List<string> _sessions = [];

    public ChatSessionCreateGroupTests()
    {
        CharacterManager.Instance.CharacterDataDictionary[CardIds[0]] = new CharacterData
        {
            CharacterId = CardIds[0], CharacterName = "初春", Description = "测试用角色",
        };
        CharacterManager.Instance.CharacterDataDictionary[CardIds[1]] = new CharacterData
        {
            CharacterId = CardIds[1], CharacterName = "佐天", Description = "测试用角色",
        };
    }

    public void Dispose()
    {
        foreach (string id in _sessions) SessionManager.Instance.Delete(id);
        foreach (string card in CardIds) CharacterManager.Instance.CharacterDataDictionary.Remove(card);
    }

    [Fact]
    public void CreateGroup_BuildsTheGroup_AndItAppearsInTheRecentList()
    {
        string parent = NewSession("主会话");

        string text = ChatSessionTools.CreateGroup(parent, "/tmp/cg-ws", "cg-评审群", ["初春", "佐天"]);

        Assert.Contains("已建群", text);
        ChatSessionMeta group = Assert.Single(SessionManager.Instance.FindSessions(x =>
            x.IsGroup && x.Title == "cg-评审群"));
        _sessions.Add(group.SessionId);
        Assert.Equal(2, SessionManager.Instance.GetGroupMembers(group.SessionId).Count);
        Assert.Contains("cg-评审群", ChatSessionTools.ListGroups(parent));
    }

    [Fact]
    public void CreateGroup_WithOneMember_IsRefused()
    {
        string parent = NewSession("主会话");

        string text = ChatSessionTools.CreateGroup(parent, null, "一个人", ["初春"]);

        Assert.StartsWith("Error:", text);
        Assert.Contains("至少", text);
        Assert.Empty(SessionManager.Instance.FindSessions(x => x.IsGroup && x.Title == "一个人"));
    }

    [Fact]
    public void CreateGroup_WithUnknownMember_NamesIt()
    {
        string parent = NewSession("主会话");

        string text = ChatSessionTools.CreateGroup(parent, null, "cg-点错人", ["初春", "不存在的角色"]);

        Assert.StartsWith("Error:", text);
        Assert.Contains("不存在的角色", text);
    }

    /// <summary>群成员与化身不建群：建群是会话这一层的事</summary>
    [Fact]
    public void CreateGroup_FromGroupMember_IsRefused()
    {
        ChatSession group = new() { Title = "群", IsGroup = true, IsAgentGroup = true };
        SessionManager.Instance.Add(group);
        _sessions.Add(group.SessionId);
        ChatSession member = new() { Title = "群 · 初春", GroupId = group.SessionId };
        SessionManager.Instance.Add(member);
        _sessions.Add(member.SessionId);

        string text = ChatSessionTools.CreateGroup(member.SessionId, null, "楼下群", ["初春", "佐天"]);

        Assert.StartsWith("Error:", text);
        Assert.Contains("群成员/化身", text);
    }

    /// <summary>SendMessage 的收件人：群按群名、带书名号的群名与群壳短号都认得（全库匹配，不认发起者）</summary>
    [Fact]
    public void SendMessage_ResolvesGroupsByNameAndShellId()
    {
        string parent = NewSession("主会话");
        ChatSessionTools.CreateGroup(parent, null, "cg-评审群", ["初春", "佐天"]);
        ChatSessionMeta group = Assert.Single(SessionManager.Instance.FindSessions(x =>
            x.IsGroup && x.Title == "cg-评审群"));
        _sessions.Add(group.SessionId);
        string shortId = SubSessionIdAlias.Short(group.SessionId);

        Assert.Equal(group.SessionId, Assert.Single(GroupMessageChannel.Match("cg-评审群")).SessionId);
        Assert.Equal(group.SessionId, Assert.Single(GroupMessageChannel.Match("《cg-评审群》")).SessionId); //书名号一起照进来也认
        Assert.Equal(group.SessionId, Assert.Single(GroupMessageChannel.Match(shortId)).SessionId);
        // list-groups 输出里的 [群壳 短号] 整行拷进 to 也认：与子代理的 [sub-session: …] 同款宽容
        Assert.Equal(group.SessionId, Assert.Single(GroupMessageChannel.Match($"[群壳 {shortId}]")).SessionId);
        Assert.Equal(group.SessionId, Assert.Single(GroupMessageChannel.Match($"《cg-评审群》[群壳 {shortId}]")).SessionId);
    }

    /// <summary>
    /// 共用建群路径允许单人成群：开发脚本的「群路径」对照场景就这么建（一个人也能把群装配跑一遍）。
    /// 「至少两位」是工具自己的产品规则（一位就是单聊），不能下沉到共用路径——那会让那些场景全红
    /// </summary>
    [Fact]
    public void GroupCreation_AllowsSingleMember_WhileTheToolDoesNot()
    {
        string parent = NewSession("主会话");

        ChatSession group = GroupCreation.Create(new GroupCreationSpec
        {
            Name = "cg-单人路径",
            Members = ["初春"],
        });
        _sessions.Add(group.SessionId);

        Assert.Single(SessionManager.Instance.GetGroupMembers(group.SessionId));
        Assert.StartsWith("Error:", ChatSessionTools.CreateGroup(parent, null, "cg-单人路径", ["初春"]));
    }

    private string NewSession(string title)
    {
        ChatSession session = new() { Title = title };
        SessionManager.Instance.Add(session);
        _sessions.Add(session.SessionId);
        return session.SessionId;
    }
}
