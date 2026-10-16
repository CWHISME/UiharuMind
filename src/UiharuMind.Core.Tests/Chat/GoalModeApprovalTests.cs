using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 目标模式的自动同意审批：无人值守的轮次里，审批一条不问、全部按同意收口，
/// 回应带上自动同意的理由（历史与日志据此认出它不是人点的）
/// </summary>
public class GoalModeApprovalTests
{
    [Fact]
    public async Task EveryRequest_IsApprovedAtOnce_WithTheAutoReason()
    {
        IReadOnlyList<ChatMessage> responses = await Resolve([Request("r1", "Write"), Request("r2", "Shell")]);

        Assert.Equal(2, responses.Count);
        foreach (ChatMessage response in responses)
        {
            ToolApprovalResponseContent content =
                Assert.Single(response.Contents.OfType<ToolApprovalResponseContent>());
            Assert.True(content.Approved);
            Assert.Equal(GoalModeApproval.Reason, content.Reason);
        }
    }

    [Fact]
    public async Task NoRequests_NothingToAnswer()
    {
        Assert.Empty(await Resolve([]));
    }

    /// <summary>
    /// 轮次上限记在通道里：一个唤醒轮共用同一个通道，要审批的轮次太多就收工——
    /// 目标模式本身没有保险丝，这一条是它仅有的审批侧闸门
    /// </summary>
    [Fact]
    public async Task TooManyRounds_EndsTheRunInsteadOfApprovingForever()
    {
        ApprovalResolver resolver = GoalModeApproval.Create("session");

        for (int i = 0; i < GoalModeApproval.MaxRounds; i++)
        {
            Assert.Single(await resolver([Request($"r{i}", "Write")]));
        }

        Assert.Empty(await resolver([Request("one-more", "Write")]));
    }

    private static Task<IReadOnlyList<ChatMessage>> Resolve(IReadOnlyList<ToolApprovalRequestContent> requests) =>
        GoalModeApproval.Create("session")(requests);

    private static ToolApprovalRequestContent Request(string id, string tool) =>
        new(id, new FunctionCallContent(id, tool, new Dictionary<string, object?>()));
}
