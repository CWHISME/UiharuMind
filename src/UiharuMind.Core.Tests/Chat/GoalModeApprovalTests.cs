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
    /// 长活不该半路收工：上限与主代理单轮工具往返同一口径（几乎无限），只防真正失控
    /// </summary>
    [Fact]
    public async Task LongRuns_AreNotCutShort()
    {
        ApprovalResolver resolver = GoalModeApproval.Create("session");

        for (int i = 0; i < 100; i++)
        {
            Assert.Single(await resolver([Request($"r{i}", "Write")]));
        }

        Assert.True(GoalModeApproval.MaxRounds >= 10000);
    }

    private static Task<IReadOnlyList<ChatMessage>> Resolve(IReadOnlyList<ToolApprovalRequestContent> requests) =>
        GoalModeApproval.Create("session")(requests);

    private static ToolApprovalRequestContent Request(string id, string tool) =>
        new(id, new FunctionCallContent(id, tool, new Dictionary<string, object?>()));
}
