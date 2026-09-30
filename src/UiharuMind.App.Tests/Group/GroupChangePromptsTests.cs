using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 建群之后改群的两道关：没跑过的群直接改，跑过的群先问（缓存代价），取消就什么都不动。
/// 消息服务由构造传入，这些弹确认的流程才测得到
/// </summary>
public class GroupChangePromptsTests : IDisposable
{
    private readonly RecordingMessageService _messages = new();
    private readonly ChatSession _group = GroupChatSessions.Create("g-prompts", false,
        [new CharacterData { CharacterName = "A" }, new CharacterData { CharacterName = "B" }], null);

    public void Dispose() => SessionManager.Instance.Delete(_group.SessionId);

    [Fact]
    public async Task Rename_NotRunYet_ChangesWithoutAsking()
    {
        Assert.True(await new GroupChangePrompts(_messages).ConfirmRenameAsync(_group, "新名"));
        Assert.Empty(_messages.Confirms);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rename_AfterRunning_AsksFirst_AndFollowsTheAnswer(bool answer)
    {
        MarkRun();
        _messages.ConfirmResult = answer;

        Assert.Equal(answer, await new GroupChangePrompts(_messages).ConfirmRenameAsync(_group, "新名"));
        Assert.Contains("新名", Assert.Single(_messages.Confirms));
    }

    /// <summary>移出是个动作，没跑过也要确认；取消了名单原样</summary>
    [Fact]
    public void Remove_AlwaysAsks_AndCancelKeepsTheRoster()
    {
        _messages.ConfirmResult = false;
        GroupMembersViewData members = new(_group, new GroupChangePrompts(_messages));

        members.Members[1].RemoveCommand.Execute(null);

        Assert.Single(_messages.Confirms);
        Assert.Equal(2, _group.GroupMemberSessionIds.Count);
    }

    [Fact]
    public void Remove_Confirmed_TakesThemOffTheRoster()
    {
        GroupMembersViewData members = new(_group, new GroupChangePrompts(_messages));
        string leaving = members.Members[1].SessionId;

        members.Members[1].RemoveCommand.Execute(null);

        Assert.DoesNotContain(leaving, _group.GroupMemberSessionIds);
    }

    // 有成员花过 token 就算跑过
    private void MarkRun() => SessionManager.Instance.Load(_group.GroupMemberSessionIds[0])!.TotalInputTokens = 100;
}
