using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 离席期间的审批（ADR 0055）：越界写入当场拒绝、化身自己的放行、其余请化身判，判不出按拒绝；每条都记账
/// </summary>
public class GroupAwayApproverTests
{
    private const string Workspace = "/tmp/uiharu-away-ws";

    private readonly ChatSession _group = new() { IsGroup = true, IsAgentGroup = true, IsTransient = true };
    private readonly ChatSession _member;
    private readonly ChatSession _avatar;
    private readonly List<GroupAwayApproval> _ledger = [];
    private readonly List<GroupAwayApprovalAsk> _asked = [];
    private Func<GroupAwayApprovalAsk, GroupAwayApprovalVerdict> _verdict = _ => new GroupAwayApprovalVerdict(true, "在推进");

    public GroupAwayApproverTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        _member = new ChatSession("Alice", new CharacterData { CharacterId = "alice", CharacterName = "Alice" })
        {
            GroupId = _group.SessionId, WorkspacePath = Workspace, IsAgentForm = true, IsTransient = true,
        };
        _avatar = new ChatSession
        {
            CharacterId = nameof(DefaultCharacter.GroupAvatarAgent), GroupId = _group.SessionId,
            WorkspacePath = Workspace, IsGroupAvatar = true, IsAgentForm = true, IsTransient = true,
        };
    }

    [Fact]
    public async Task OutOfWorkspaceWrite_IsDeniedWithoutAsking()
    {
        IReadOnlyList<ChatMessage> responses = await Resolve(_member, Call("Write", "/etc/hosts"));

        Assert.False(Approved(responses));
        Assert.Empty(_asked);
        Assert.Contains("越界写入", Assert.Single(_ledger).Reason);
    }

    [Fact]
    public async Task AvatarsOwnCall_IsApprovedWithoutAsking()
    {
        IReadOnlyList<ChatMessage> responses = await Resolve(_avatar, Call("Shell", null));

        Assert.True(Approved(responses));
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task MembersCall_IsJudgedByTheAvatar_AndRecorded()
    {
        _verdict = _ => new GroupAwayApprovalVerdict(false, "要推送，等本人");

        IReadOnlyList<ChatMessage> responses = await Resolve(_member, Call("Shell", null));

        Assert.False(Approved(responses));
        Assert.Equal("Alice", Assert.Single(_asked).RequesterName);
        Assert.Equal(new GroupAwayApproval("Alice", "Shell", false, "要推送，等本人"), Assert.Single(_ledger));
    }

    [Fact]
    public async Task JudgeFailure_Denies()
    {
        _verdict = _ => throw new InvalidOperationException("429");

        Assert.False(Approved(await Resolve(_member, Call("Shell", null))));
        Assert.Contains("没判出来", Assert.Single(_ledger).Reason);
    }

    [Fact]
    public async Task ThreeDeniedRoundsInARow_EndTheTurn()
    {
        _verdict = _ => new GroupAwayApprovalVerdict(false, "不行");
        ApprovalResolver resolver = Approver().Create(_group, _avatar, _member, _ledger.Add, CancellationToken.None);

        for (int i = 0; i < 3; i++) Assert.Single(await resolver([Request(Call("Shell", null))]));

        Assert.Empty(await resolver([Request(Call("Shell", null))]));
    }

    [Fact]
    public void UserWords_StartAtTheLatestGoal_AndSkipWhatTheAvatarSaid()
    {
        ChatMessage oldGoal = new(ChatRole.User, $"{GroupAvatarTranscript.AwayGoalTag} 旧目标");
        ChatMessage goal = new(ChatRole.User, $"{GroupAvatarTranscript.AwayGoalTag} 新目标");
        ChatMessage byAvatar = new(ChatRole.User, "就用 A");
        ChatMessageAnnotations.MarkGroupAvatarPost(byAvatar, "avatar");
        ChatMessage member = new(ChatRole.Assistant, "好");
        ChatMessage later = new(ChatRole.User, "推送等我回来");

        IReadOnlyList<ChatMessage> words = AvatarApprovalJudge.UserWordsSinceGoal([oldGoal, goal, byAvatar, member, later]);

        Assert.Equal([goal, later], words);
    }

    [Theory]
    [InlineData("批准\n在推进目标", true, "在推进目标")]
    [InlineData("拒绝\n要推送", false, "要推送")]
    [InlineData("我觉得可以", false, "我觉得可以")]
    [InlineData("", false, "化身没给出判定，按拒绝处理")]
    public void Answer_IsReadStrictly(string answer, bool approved, string reason)
    {
        Assert.Equal(new GroupAwayApprovalVerdict(approved, reason), AvatarApprovalJudge.Parse(answer));
    }

    private Task<IReadOnlyList<ChatMessage>> Resolve(ChatSession requester, FunctionCallContent call) =>
        Approver().Create(_group, _avatar, requester, _ledger.Add, CancellationToken.None)([Request(call)]);

    private GroupAwayApprover Approver() => new((ask, _) =>
    {
        _asked.Add(ask);
        return Task.FromResult(_verdict(ask));
    }, group => group.History);

    private static FunctionCallContent Call(string tool, string? path) =>
        new("c1", tool, path == null
            ? new Dictionary<string, object?> { ["command"] = "git push" }
            : new Dictionary<string, object?> { ["filePath"] = path, ["content"] = "x" });

    private static ToolApprovalRequestContent Request(FunctionCallContent call) => new("r1", call);

    private static bool Approved(IReadOnlyList<ChatMessage> responses) =>
        Assert.Single(Assert.Single(responses).Contents.OfType<ToolApprovalResponseContent>()).Approved;
}
