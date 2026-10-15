using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>目标模式结束工具（隐藏扩展的内层）：认 reason/summary，认不出为 null</summary>
public class EndGoalModeToolTests
{
    [Fact]
    public void Read_Done_ReturnsDoneRequest()
    {
        FunctionCallContent call = new("c1", EndGoalModeTool.ToolName,
            new Dictionary<string, object?> { ["reason"] = "done", ["summary"] = "写完了报告" });

        GoalModeEndCall? end = EndGoalModeTool.Read(call);
        Assert.NotNull(end);
        Assert.Equal(EGoalModeEndReason.Done, end.Reason);
        Assert.Equal("写完了报告", end.Summary);
    }

    [Fact]
    public void Read_NeedsUser_ReturnsNeedsUserRequest()
    {
        FunctionCallContent call = new("c2", EndGoalModeTool.ToolName,
            new Dictionary<string, object?> { ["reason"] = "needs_user", ["summary"] = "需要你确认发布" });

        GoalModeEndCall? end = EndGoalModeTool.Read(call);
        Assert.NotNull(end);
        Assert.Equal(EGoalModeEndReason.NeedsUser, end.Reason);
        Assert.Equal("需要你确认发布", end.Summary);
    }

    [Fact]
    public void Read_UnknownReason_ReturnsNull()
    {
        FunctionCallContent call = new("c3", EndGoalModeTool.ToolName,
            new Dictionary<string, object?> { ["reason"] = "whatever", ["summary"] = "x" });

        Assert.Null(EndGoalModeTool.Read(call));
    }

    [Fact]
    public void Read_WrongToolName_ReturnsNull()
    {
        FunctionCallContent call = new("c4", "SomeOtherTool",
            new Dictionary<string, object?> { ["reason"] = "done", ["summary"] = "x" });

        Assert.Null(EndGoalModeTool.Read(call));
    }

    [Fact]
    public void Read_MissingSummary_ToleratedAsEmpty()
    {
        FunctionCallContent call = new("c5", EndGoalModeTool.ToolName,
            new Dictionary<string, object?> { ["reason"] = "done" });

        GoalModeEndCall? end = EndGoalModeTool.Read(call);
        Assert.NotNull(end);
        Assert.Equal(string.Empty, end.Summary);
    }
}
