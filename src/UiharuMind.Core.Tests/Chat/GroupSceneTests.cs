using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群场景段进系统提示（ADR 0048）：正文由谁生成、插在哪、变了会不会重建。
/// </summary>
public class GroupSceneTests
{
    private static readonly Dictionary<string, CharacterData?> Characters = new()
    {
        ["a"] = new CharacterData { CharacterName = "Alice" },
        ["b"] = new CharacterData { CharacterName = "Bob" },
        ["c"] = new CharacterData { CharacterName = "Carol" },
    };

    [Fact]
    public void Scene_ListsTheRoomAndTheRules()
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", [new GroupMemberPresence("Bob", "")], "我", false, null));

        Assert.Contains("群聊「会审」", scene);
        Assert.Contains("我（用户）、Bob", scene);
        Assert.Contains("你是Alice", scene);
        Assert.Contains("自动标上你的名字", scene);
        Assert.DoesNotContain("不要自己加", scene); //禁令式会把前缀格式再念一遍，改用正面说法
        Assert.DoesNotContain("主持人", scene);
        Assert.DoesNotContain(GroupPostTool.ToolName, scene); //没这个工具就不提
        Assert.DoesNotContain("一轮怎么算", scene); //普通形态没工具，「过程话、查完再说」都无从谈起
        Assert.Contains("不写成报告", scene);
    }

    /// <summary>回复两种形态都是发言、「[沉默]」都是不接话；有群发言工具的多讲一条中途说话（ADR 0060 修订）</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scene_ReplyIsSpeechForBothForms_ToolOnlyForMidTurn(bool hasTool)
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", [new GroupMemberPresence("Bob", "")], "我", hasTool, null));

        Assert.Contains("你每次说完的正文就是你在群里说的话", scene);
        Assert.Contains(GroupTranscript.PassReply, scene);
        Assert.Equal(hasTool, scene.Contains("中途说话"));
    }

    [Theory]
    [InlineData("Alice", "你是本群主持人")]
    [InlineData("Bob", "本群主持人是Bob")]
    public void Scene_TellsWhoTheHostIs(string host, string expected)
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", [new GroupMemberPresence("Bob", "")], "我", true, host));

        Assert.Contains(expected, scene);
        Assert.Contains(GroupPostTool.ToolName, scene);
        if (host == "Alice")
            Assert.Contains("点完名这一轮就结束", scene); //主持人自己的收尾与查资料规矩只在场景段讲，不再随投递插一句
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scene_TalksAboutTheWorkspace_OnlyWhenHeCanTouchIt(bool shares)
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", [new GroupMemberPresence("Bob", "")], "我", false, null, shares));

        Assert.Equal(shares, scene.Contains("草稿目录是全群共用的"));
        Assert.Equal(shares, scene.Contains("写成草稿目录里的文件")); //长材料有地方放，才让他挪出去
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Scene_AsksToClaimWork_OnlyWhenHeCanTouchTheWorkspaceAndPostMidTurn(bool shares, bool canPost, bool expected)
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice", [new GroupMemberPresence("Bob", "")], "我", canPost, null, shares));

        Assert.Equal(expected, scene.Contains("没点名谁做时，先调用"));
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
    public void Scene_GroupsMembersOfTheSameWorks()
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice",
            [new GroupMemberPresence("Bob", "魔法禁书目录"), new GroupMemberPresence("Carol", "魔法禁书目录"),
                new GroupMemberPresence("Dave", "死亡笔记"), new GroupMemberPresence("Eve", "")],
            "我", false, null));

        Assert.Contains("我（用户）、Eve、《魔法禁书目录》：Bob、Carol、《死亡笔记》：Dave", scene);
    }

    /// <summary>
    /// 裸名（没填作品）必须摆在分组前面：分组没有右边界，跟在后面的裸名会被误读成组里人
    /// （实测 OP-01 被算进《魔法禁书目录》就是这么来的）。
    /// </summary>
    [Fact]
    public void Scene_BareMemberAfterAGroup_IsNotSwallowedByIt()
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("聊天群", "白井黑子",
            [new GroupMemberPresence("御坂美琴", "魔法禁书目录"), new GroupMemberPresence("佐天泪子", "魔法禁书目录"),
                new GroupMemberPresence("初春饰利", "魔法禁书目录"), new GroupMemberPresence("OP-01", "")],
            "黑猫", false, null));

        Assert.Contains("黑猫（用户）、OP-01、《魔法禁书目录》：御坂美琴、佐天泪子、初春饰利", scene);
        Assert.DoesNotContain("初春饰利、OP-01", scene);
    }

    [Fact]
    public void Scene_WithoutWorks_StaysAsBefore()
    {
        string scene = GroupTranscript.BuildScene(new GroupScene("会审", "Alice",
            [new GroupMemberPresence("Bob", ""), new GroupMemberPresence("Carol", " ")], "我", false, null));

        Assert.Contains("我（用户）、Bob、Carol", scene);
        Assert.DoesNotContain("《", scene.Split('\n')[0]);
    }

    [Fact]
    public void Source_ResolvesTheMembersGroup()
    {
        (ChatSession group, ChatSession alice) = NewGroup(host: "c");

        string scene = GroupSceneSource.For(alice, group, MemberOf(Characters), "我");

        Assert.Contains("我（用户）、Bob、Carol", scene);
        Assert.Contains("本群主持人是Carol", scene);
    }

    [Fact]
    public void Source_GroupsMembersOfTheSameWorks()
    {
        (ChatSession group, ChatSession alice) = NewGroup(host: null);
        Dictionary<string, CharacterData?> characters = new()
        {
            ["a"] = alice.CharacterData,
            ["b"] = new CharacterData { CharacterName = "Bob", Works = "魔法禁书目录" },
            ["c"] = new CharacterData { CharacterName = "Carol", Works = "魔法禁书目录" },
        };

        string scene = GroupSceneSource.For(alice, group, MemberOf(characters), "我");

        Assert.Contains("《魔法禁书目录》：Bob、Carol", scene);
    }

    /// <summary>在场的人按入群先后列，与发言顺序无关：调了顺序系统提示不变</summary>
    [Fact]
    public void Source_ListsByJoinOrder_NotBySpeakingOrder()
    {
        (ChatSession group, ChatSession alice) = NewGroup(host: null);
        string before = GroupSceneSource.For(alice, group, MemberOf(Characters), "我");

        group.GroupMemberSessionIds = ["c", "a", "b"];

        Assert.Equal(before, GroupSceneSource.For(alice, group, MemberOf(Characters), "我"));
        Assert.Contains("我（用户）、Bob、Carol", before);
    }

    [Fact]
    public void Source_IsEmptyOutsideTheGroup()
    {
        (ChatSession group, ChatSession alice) = NewGroup(host: null);

        Assert.Equal("", GroupSceneSource.For(new ChatSession { IsTransient = true }, group, MemberOf(Characters), "我"));
        Assert.Equal("", GroupSceneSource.For(alice, null, MemberOf(Characters), "我"));
        Assert.Equal("", GroupSceneSource.For(alice, new ChatSession { IsGroup = true, IsTransient = true },
            MemberOf(Characters), "我"));
    }

    /// <summary>换主持人 → 场景段变 → 快照不等，下一次挂接重建；两种形态都一样</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangingTheScene_RebuildsTheAssembly(bool isAgentForm)
    {
        CharacterData character = new() { CharacterId = "x", IsAgent = true };
        AgentAssemblyFacts Capture(string scene) => AgentAssemblyFacts.Capture(character,
            new AgentAssemblyInputs
            {
                Instructions = "prompt", WorkspacePath = "/ws", Permission = EAgentPermissionMode.AutoEdit,
                McpRevision = 1, IsAgentForm = isAgentForm, GroupScene = scene,
            });

        Assert.Equal(Capture("主持人是 Bob"), Capture("主持人是 Bob"));
        Assert.NotEqual(Capture("主持人是 Bob"), Capture("主持人是 Carol"));
    }

    /// <summary>
    /// 群成员不委派，中途发群走只收正文的专用工具：共用 SendMessage 时收件人一填错
    /// （写成用户名、留空）就静默派出一个群里看不见、也停不了的子代理
    /// </summary>
    [Theory]
    [InlineData("场景正文", true)]
    [InlineData("", false)]
    public async Task GroupMembers_PostToTheGroup_InsteadOfDelegating(string scene, bool isMember)
    {
        CharacterData character = new()
        {
            CharacterId = "x", IsAgent = true,
            Tools = new AgentToolConfig { EnableShellExecution = false, EnableFileAccess = false, EnableWebSearch = false },
        };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile { Character = character, SessionId = "member1", GroupScene = scene },
            WorkingDirectory = Path.GetTempPath(),
        };

        await using AgentHandle handle = AgentAssembler.Assemble(plan);

        // 群发言工具与委派同名（SendMessage），按参数认：委派要填收件人 to，群发言只收正文
        List<AIFunction> tools = handle.ChatOptions?.Tools?.OfType<AIFunction>().ToList() ?? [];
        bool Delegates(AIFunction x) => x.JsonSchema.GetRawText().Contains("\"to\"");
        Assert.Equal(isMember, tools.Any(x => x.Name == GroupPostTool.ToolName && !Delegates(x)));
        Assert.Equal(!isMember, tools.Any(x => x.Name == SubAgentTool.ToolName && Delegates(x)));
        Assert.True(character.Tools.EnableSubAgent); //只是这次装配不给，角色卡本身不改
        Assert.Equal(!isMember, AgentAssemblyFacts.Capture(character,
            new AgentAssemblyInputs
            {
                Instructions = "prompt", WorkspacePath = "/ws", Permission = EAgentPermissionMode.AutoEdit,
                McpRevision = 1, IsAgentForm = true, GroupScene = scene,
            }).SubAgent);
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

    /// <summary>运行宿主是人格写不出来的事实：不说，模型对不上描述里写着「UiharuMind 这个应用」的内置技能（ADR 0061）</summary>
    [Fact]
    public void AgentPrompt_BaseSaysWhereTheModelRuns()
    {
        Compose("", out IReadOnlyList<AgentPromptSegment> segments);

        Assert.Contains(AgentBasePrompts.HostFact, segments.Single(x => x.Section == EPromptSection.Base).Text);
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
        AgentInstructionsComposer.Compose("我是 Alice", scene, new AgentToolConfig(), "/tmp/uiharu-scene-test",
            "", "", "", "", "", "", "", "", out segments);

    // 入群先后按标识字母序：a 最早、c 最晚
    private static Func<string, GroupRosterMember?> MemberOf(IReadOnlyDictionary<string, CharacterData?> characters) =>
        id => characters.GetValueOrDefault(id) is { } character
            ? new GroupRosterMember(character, new ChatSessionMeta
            {
                SessionId = id,
                CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(id[0]),
            })
            : null;

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
