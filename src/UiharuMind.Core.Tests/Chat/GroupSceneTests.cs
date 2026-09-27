using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群场景段进系统提示（ADR 0048）：正文由谁生成、插在哪、变了会不会重建。
/// </summary>
public class GroupSceneTests
{
    private static readonly Dictionary<string, string> Names = new() { ["a"] = "Alice", ["b"] = "Bob", ["c"] = "Carol" };

    [Fact]
    public void Scene_ListsTheRoomAndTheRules()
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", ["Bob"], "我", false, null));

        Assert.Contains("群聊「会审」", scene);
        Assert.Contains("我（用户）、Bob", scene);
        Assert.Contains("你是Alice", scene);
        Assert.Contains("不要自己加「[名字]:」前缀", scene);
        Assert.DoesNotContain("主持人", scene);
        Assert.DoesNotContain("SendMessage", scene); //没这个工具就不提
    }

    [Theory]
    [InlineData("Alice", "你是本群主持人")]
    [InlineData("Bob", "本群主持人是Bob")]
    public void Scene_TellsWhoTheHostIs(string host, string expected)
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", ["Bob"], "我", true, host));

        Assert.Contains(expected, scene);
        Assert.Contains("SendMessage", scene);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scene_TalksAboutTheWorkspace_OnlyWhenHeCanTouchIt(bool shares)
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", ["Bob"], "我", false, null, shares));

        Assert.Equal(shares, scene.Contains("草稿目录是全群共用的"));
        Assert.Equal(shares, scene.Contains("方案由用户拍板")); //动得了工作区的人才需要这条
    }

    /// <summary>群成员的产出落群壳那一间：一起干的活在一处，不必从各人目录里拼</summary>
    [Fact]
    public void Members_ShareTheGroupsOutputRoom()
    {
        ChatSession Member(string id) => new() { SessionId = id, GroupId = "group123456", WorkspacePath = "/ws", IsTransient = true };

        string alice = AgentBuildProfile.FromSession(Member("alice0001")).OutputFolderName;
        string bob = AgentBuildProfile.FromSession(Member("bob000001")).OutputFolderName;
        string solo = AgentBuildProfile.FromSession(new ChatSession { SessionId = "alice0001", WorkspacePath = "/ws" }).OutputFolderName;

        Assert.Equal(AgentOutputLayout.GetFolderName("/ws", "group123456"), alice);
        Assert.Equal(alice, bob);
        Assert.Equal(AgentOutputLayout.GetFolderName("/ws", "alice0001"), solo);
    }

    [Fact]
    public void Source_ResolvesTheMembersGroup()
    {
        (ChatSession group, ChatSession alice) = NewGroup(host: "c");

        string scene = GroupSceneSource.For(alice, group, Names.GetValueOrDefault, "我");

        Assert.Contains("我（用户）、Bob、Carol", scene);
        Assert.Contains("本群主持人是Carol", scene);
    }

    [Fact]
    public void Source_IsEmptyOutsideTheGroup()
    {
        (ChatSession group, ChatSession alice) = NewGroup(host: null);

        Assert.Equal("", GroupSceneSource.For(new ChatSession { IsTransient = true }, group, Names.GetValueOrDefault, "我"));
        Assert.Equal("", GroupSceneSource.For(alice, null, Names.GetValueOrDefault, "我"));
        Assert.Equal("", GroupSceneSource.For(alice, new ChatSession { IsGroup = true, IsTransient = true },
            Names.GetValueOrDefault, "我"));
    }

    /// <summary>换主持人 → 场景段变 → 快照不等，下一次挂接重建；两种形态都一样</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangingTheScene_RebuildsTheAssembly(bool isAgentForm)
    {
        CharacterData character = new() { CharacterId = "x", IsAgent = true };
        AgentAssemblyFacts Capture(string scene) => AgentAssemblyFacts.Capture(character, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1, isAgentForm: isAgentForm, groupScene: scene);

        Assert.Equal(Capture("主持人是 Bob"), Capture("主持人是 Bob"));
        Assert.NotEqual(Capture("主持人是 Bob"), Capture("主持人是 Carol"));
    }

    [Fact]
    public void AgentPrompt_PutsTheSceneBetweenPersonaAndTools()
    {
        string instructions = Compose("场景正文", out IReadOnlyList<AgentPromptSegment> segments);

        Assert.Equal([EPromptSection.Base, EPromptSection.Character, EPromptSection.Scene],
            segments.Select(x => x.Section).Take(3));
        AgentPromptSegment scene = segments.Single(x => x.Section == EPromptSection.Scene);
        Assert.Equal($"{AgentPromptHeadings.Scene}\n\n场景正文", scene.Text);
        Assert.Contains(scene.Text, instructions);
    }

    [Fact]
    public void AgentPrompt_WithoutGroup_HasNoScene()
    {
        string instructions = Compose("", out IReadOnlyList<AgentPromptSegment> segments);

        Assert.DoesNotContain(segments, x => x.Section == EPromptSection.Scene);
        Assert.DoesNotContain(AgentPromptHeadings.Scene, instructions);
    }

    /// <summary>普通角色不走 Compose（没有基座与工具纪律），场景接在角色提示末尾</summary>
    [Fact]
    public async Task PromptOnly_AppendsTheSceneToThePersona()
    {
        CharacterData character = new() { CharacterId = "writer", IsAgent = false, Template = "我是写手" };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile { Character = character, GroupScene = "场景正文" },
        };

        await using AgentHandle handle = AgentAssembler.Assemble(plan);

        string instructions = handle.ChatOptions?.Instructions ?? "";
        Assert.StartsWith("我是写手", instructions);
        Assert.EndsWith($"{AgentPromptHeadings.Scene}\n\n场景正文", instructions);
        Assert.DoesNotContain(AgentPromptHeadings.Base, instructions);
    }

    [Fact]
    public void RoleplaySnapshot_ReportsTheSceneSegment()
    {
        CharacterData character = new() { CharacterId = "writer", Template = "我是写手" };

        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.FromRoleplay(character, null, "场景正文");

        Assert.True(snapshot.PromptTokensOf(EPromptSection.Scene) > 0);
        Assert.DoesNotContain(AgentCapabilitySnapshot.FromRoleplay(character).PromptSegments,
            x => x.Section == EPromptSection.Scene);
    }

    private static string Compose(string scene, out IReadOnlyList<AgentPromptSegment> segments) =>
        AgentInstructionsComposer.Compose("我是 Alice", scene, new AgentToolConfig(), false, "/tmp/uiharu-scene-test",
            "", "", "", "", "", "", "", "", out segments);

    private static (ChatSession Group, ChatSession Alice) NewGroup(string? host)
    {
        ChatSession group = new() { Title = "会审", IsGroup = true, IsTransient = true };
        ChatSession alice = new("Alice", new CharacterData { CharacterId = "alice", CharacterName = "Alice" })
        {
            SessionId = "a", GroupId = group.SessionId, IsTransient = true,
        };
        group.GroupMemberSessionIds = ["a", "b", "c"];
        group.GroupHostSessionId = host;
        return (group, alice);
    }
}
