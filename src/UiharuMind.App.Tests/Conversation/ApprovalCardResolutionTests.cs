using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 审批卡收起后那一行显示的是文案，不是决定代号（从前直接露出 once / deny）
/// </summary>
public class ApprovalCardResolutionTests
{
    private static ToolApprovalRequestContent Request() => new("c1", new FunctionCallContent("c1", "Shell", null));

    [Fact]
    public void ClickedDecision_ShowsItsLabel()
    {
        ApprovalRequestItem card = new(Request());

        card.ResolveCommand.Execute("deny");

        Assert.True(card.IsResolved);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedDenied), card.ResolvedText);
    }

    /// <summary>别处（群的待审批条）先批了：这张收起，显示那个决定</summary>
    [Fact]
    public void DecidedElsewhere_FoldsWithThatDecision()
    {
        ToolApprovalRequestContent request = Request();
        ApprovalRequestItem card = new(request);

        card.MarkDecidedElsewhere(new ChatMessage(ChatRole.User,
            [ToolApprovalResponseFactory.Create(request, EApprovalDecision.Once, "ok")]));

        Assert.True(card.IsResolved);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedOnce), card.ResolvedText);
        Assert.True(card.Response.IsCompleted);
    }
}
