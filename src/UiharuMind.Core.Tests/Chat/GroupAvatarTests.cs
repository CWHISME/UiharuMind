using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群的化身会话（ADR 0055）：挂在群上、跟群走权限与工作区，但不进名单；投递跳过它自己的话
/// </summary>
public class GroupAvatarTests
{
    public GroupAvatarTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
    }

    [Fact]
    public void Avatar_IsOnePerGroup_FollowsTheGroup_AndStaysOffTheRoster()
    {
        ChatSession group = GroupChatSessions.Create("g-avatar", true, Cards("a", "b"), "/tmp/g-avatar-ws");
        try
        {
            ChatSession avatar = GroupAvatar.EnsureFor(group, "model-a");
            ChatSession again = GroupAvatar.EnsureFor(group, "model-b");

            Assert.Same(avatar, again);
            Assert.Equal("model-b", again.SessionModelName); //每次离席换上这次选的模型
            Assert.True(avatar.IsGroupAvatar);
            Assert.Equal("/tmp/g-avatar-ws", GroupChatSessions.WorkspaceOf(avatar));
            Assert.Equal(nameof(DefaultCharacter.GroupAvatarAgent), avatar.CharacterId);

            GroupRoster roster = GroupRoster.Of(group);
            Assert.DoesNotContain(avatar.SessionId, roster.Everyone.Select(x => x.SessionId));
            ChatSessionMeta meta = SessionManager.Instance.GetMeta(group.SessionId)!;
            meta.GroupMemberSessionIds = []; //旧索引的回退路径也不许把它当成员
            Assert.DoesNotContain(avatar.SessionId, GroupRoster.Of(meta).Present.Select(x => x.SessionId));
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }

        Assert.Null(GroupAvatar.MetaOf(group.SessionId)); //随群级联删除
    }

    [Fact]
    public void NewAvatar_OnlyCatchesUpOnTheRecentBacklog()
    {
        ChatSession group = new() { IsGroup = true, IsAgentGroup = true, IsTransient = true };
        for (int i = 0; i < GroupAvatar.InitialBacklog + 5; i++) group.History.Add(new ChatMessage(ChatRole.User, $"{i}"));

        Assert.Equal(5, GroupAvatar.New(group, null).GroupCursor);
    }

    [Fact]
    public void Delivery_SkipsWhatTheAvatarItselfSaid()
    {
        ChatMessage byAvatar = new(ChatRole.User, "就用方案 A") { AuthorName = "黑猫" };
        ChatMessageAnnotations.MarkGroupAvatarPost(byAvatar, "avatar");
        ChatMessage reply = new(ChatRole.Assistant, "好，我来改") { AuthorName = "Alice" };
        ChatMessageAnnotations.MarkGroupPost(reply, "alice", "alice-session");

        string? toAvatar = GroupTranscript.BuildDelivery([byAvatar, reply], 0, "avatar");
        string? toAlice = GroupTranscript.BuildDelivery([byAvatar, reply], 0, "alice-session");

        Assert.Equal(GroupTranscript.FeedHeader + "[Alice]: 好，我来改", toAvatar);
        Assert.Equal(GroupTranscript.FeedHeader + "[黑猫]: 就用方案 A", toAlice); //成员看到的就是用户说的
    }

    [Fact]
    public void Scene_SitsInForTheUser_AndListsEveryMember()
    {
        ChatSession group = new() { Title = "会审", IsGroup = true, IsTransient = true };
        group.GroupMemberSessionIds = ["a", "b"];
        group.GroupHostSessionId = "b";
        ChatSession avatar = new() { GroupId = group.SessionId, IsGroupAvatar = true, IsTransient = true };
        Dictionary<string, string> names = new() { ["a"] = "Alice", ["b"] = "Bob" };

        string scene = GroupSceneSource.For(avatar, group, id => names.TryGetValue(id, out string? name)
            ? new GroupRosterMember(new CharacterData { CharacterName = name },
                new ChatSessionMeta { SessionId = id, CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(id[0]) })
            : null, "黑猫");

        Assert.Contains("替黑猫坐着", scene);
        Assert.Contains("在场的成员有：Alice、Bob", scene);
        Assert.Contains("本群主持人是Bob", scene);
        Assert.DoesNotContain("（用户）", scene); //它就是用户
        Assert.DoesNotContain(GroupPostTool.ToolName, scene);
    }

    /// <summary>化身不往群里发言（它说完的话由离席以用户名义发）、不委派，多一把结束离席</summary>
    [Fact]
    public async Task Assembly_GivesTheAvatarEndAway_NotGroupPostOrDelegation()
    {
        CharacterData character = new()
        {
            CharacterId = "avatar", IsAgent = true,
            Tools = new AgentToolConfig { EnableShellExecution = false, EnableFileAccess = false, EnableWebSearch = false },
        };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile
            {
                Character = character, SessionId = "avatar1", GroupScene = "场景正文", IsGroupAvatar = true,
            },
            WorkingDirectory = Path.GetTempPath(),
        };

        await using AgentHandle handle = AgentAssembler.Assemble(plan);

        List<string> tools = handle.ChatOptions?.Tools?.Select(x => x.Name).ToList() ?? [];
        Assert.Contains(EndAwayTool.ToolName, tools);
        Assert.DoesNotContain(GroupPostTool.ToolName, tools);
        Assert.DoesNotContain(SubAgentTool.ToolName, tools);
    }

    [Theory]
    [InlineData("done", EAwayEndRequest.Done)]
    [InlineData(" needs_user ", EAwayEndRequest.NeedsUser)]
    [InlineData("later", null)]
    public void EndAway_IsReadBackFromTheCall(string reason, EAwayEndRequest? expected)
    {
        FunctionCallContent call = new("c1", EndAwayTool.ToolName,
            new Dictionary<string, object?> { ["reason"] = reason, ["summary"] = " 都做完了 " });

        AwayEndRequest? request = EndAwayTool.Read(call);

        Assert.Equal(expected, request?.Reason);
        if (request != null) Assert.Equal("都做完了", request.Summary);
    }

    [Fact]
    public async Task EndAway_InfiniteMode_ReturnsAnError()
    {
        AIFunction tool = (AIFunction)EndAwayTool.Create(() => true);

        object? result = await tool.InvokeAsync(new AIFunctionArguments { ["reason"] = "done", ["summary"] = "做完了" },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("Error: infinite away mode is on", result?.ToString());
    }

    [Fact]
    public async Task EndAway_NormalMode_Acknowledges()
    {
        AIFunction tool = (AIFunction)EndAwayTool.Create(() => false);

        object? result = await tool.InvokeAsync(new AIFunctionArguments { ["reason"] = "done", ["summary"] = "做完了" },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("Away session will end after this turn", result?.ToString());
    }

    [Fact]
    public void BriefingNote_KickoffOnlyFirstTurn_ReminderAndInfiniteEveryTurn()
    {
        string? first = GroupAvatarTranscript.BriefingNote("黑猫", "把登录页修了", "这类改动可以替我拍", true);
        string? later = GroupAvatarTranscript.BriefingNote("黑猫", "把登录页修了", "这类改动可以替我拍", true,
            kickoff: false);

        Assert.Contains("把登录页修了", first);
        Assert.DoesNotContain("把登录页修了", later);
        Assert.Contains("这类改动可以替我拍", first);
        Assert.Contains("这类改动可以替我拍", later);
        Assert.Contains("无限模式", later);
        Assert.Null(GroupAvatarTranscript.BriefingNote("黑猫", "  ", null, false, kickoff: false));
    }

    private static IReadOnlyList<CharacterData> Cards(params string[] ids) =>
        ids.Select(x => new CharacterData { CharacterId = $"avatar-{x}", CharacterName = x }).ToList();
}
