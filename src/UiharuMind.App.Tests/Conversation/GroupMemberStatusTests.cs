using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 群成员行上的「网络失败」：这一轮的空白要靠它解释，所以它只在「没在跑、也没在等审批」时露出来，
/// 开跑或下一轮跑成之后必须自己撤掉——一直挂着的话，用户会以为这个人坏了
/// </summary>
public class GroupMemberStatusTests
{
    /// <summary>标上之后，空闲状态看得到</summary>
    [Fact]
    public void Marked_Idle_Shows()
    {
        GroupMemberItem item = NewItem();

        item.MarkTurnOutcome(true);

        Assert.True(item.IsNetworkFailed);
        Assert.True(item.ShowsNetworkFailed);
    }

    /// <summary>下一轮开跑就藏（让位给「发言中」），但标还留着：跑完没成的话还要继续说明</summary>
    [Fact]
    public void Running_HidesButKeepsTheMark()
    {
        GroupMemberItem item = NewItem();
        item.MarkTurnOutcome(true);

        item.IsRunning = true;

        Assert.False(item.ShowsNetworkFailed);
        Assert.True(item.IsNetworkFailed);

        item.IsRunning = false;
        Assert.True(item.ShowsNetworkFailed);
    }

    /// <summary>等审批时让位：那一行同时只挂一个状态</summary>
    [Fact]
    public void AwaitingApproval_Hides()
    {
        GroupMemberItem item = NewItem();
        item.MarkTurnOutcome(true);

        item.IsAwaitingApproval = true;

        Assert.False(item.ShowsNetworkFailed);
    }

    /// <summary>跑成了一次就撤标（失败之后的恢复）</summary>
    [Fact]
    public void Recovery_Clears()
    {
        GroupMemberItem item = NewItem();
        item.MarkTurnOutcome(true);

        item.MarkTurnOutcome(false);

        Assert.False(item.IsNetworkFailed);
        Assert.False(item.ShowsNetworkFailed);
    }

    private static GroupMemberItem NewItem() => new(
        new ChatSessionMeta
        {
            SessionId = Guid.NewGuid().ToString("N"),
            CharacterId = nameof(DefaultCharacter.None),
        },
        isHost: false, _ => { }, (_, _) => { });
}
