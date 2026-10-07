using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 工具卡失败原因的中文行：派生自结果正文（重放、重启都在），只解释“调用没跑”
/// 那几种审批收口；英文原文照旧显示，这一行动机只说人话
/// </summary>
public class ToolCallItemFailureHintTests
{
    private static ToolCallItem FailedCard(string resultText) =>
        new() { ToolName = "Write", IsSuccess = false, ResultText = resultText };

    [Fact]
    public void ApprovalUnanswered_ExplainsNobodyAnswered()
    {
        ToolCallItem item = FailedCard(ToolCallCancellation.ApprovalUnansweredResultText);

        Assert.Equal(Loc.Text(LangKey.ToolResultUnansweredHint), item.ResultFailureHint);
    }

    [Fact]
    public void WakeUnanswered_ExplainsNoWindow()
    {
        // 唤醒轮没有打开的窗口,审批无处可弹——解释「为什么没弹」而不是笼统的「无人回应」
        ToolCallItem item = FailedCard(ToolCallCancellation.WakeUnansweredResultText);

        Assert.Equal(Loc.Text(LangKey.ToolResultWakeUnansweredHint), item.ResultFailureHint);
    }

    [Fact]
    public void Rejected_ExplainsGenericRejection()
    {
        // 不细分拒绝原因:非用户拒绝的措辞(策略拒绝、出错)细分只会误标,英文原文已如实写明
        ToolCallItem item = FailedCard("Tool call invocation rejected. Workspace policy");

        Assert.Equal(Loc.Text(LangKey.ToolResultRejectedHint), item.ResultFailureHint);
    }

    [Fact]
    public void SuccessfulResult_NeverShowsHint()
    {
        // 正文恰好撞上标记文本的文件照常显示：成功的结果不解释
        ToolCallItem item = new()
        {
            ToolName = "Read",
            IsSuccess = true,
            ResultText = ToolCallCancellation.ApprovalUnansweredResultText,
        };

        Assert.Equal(string.Empty, item.ResultFailureHint);
    }

    [Fact]
    public void OrdinaryFailure_ShowsNoHint()
    {
        ToolCallItem item = FailedCard("File not found");

        Assert.Equal(string.Empty, item.ResultFailureHint);
    }
}
