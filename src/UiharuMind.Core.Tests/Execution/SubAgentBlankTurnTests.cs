using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

public class SubAgentBlankTurnTests
{
    private static readonly DateTimeOffset Epoch = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BlankTurnStreak_IsAnchoredAtFirstTurnAndExpiresAfterTenMinutes()
    {
        ChatSession session = NewSession(Guid.NewGuid().ToString("N"));
        SubAgentTool.RecordBlankTurn(session.SessionId, Epoch);
        SubAgentTool.RecordBlankTurn(session.SessionId, Epoch.AddMinutes(9));

        Assert.NotNull(SubAgentTool.TryBuildBlankTurnRefusal(session, Epoch.AddMinutes(9)));
        Assert.Null(SubAgentTool.TryBuildBlankTurnRefusal(session, Epoch.AddMinutes(10).AddTicks(1)));
    }

    [Fact]
    public void SuccessfulNonemptyTurn_ClearsBlankTurnStreak()
    {
        ChatSession session = NewSession(Guid.NewGuid().ToString("N"));
        SubAgentTool.RecordBlankTurn(session.SessionId, Epoch);

        SubAgentTool.ReportAccumulator report = new();
        report.Add(new TextContent("已完成"));
        if (!report.ProducedNothing) SubAgentTool.ClearBlankTurn(session.SessionId);
        SubAgentTool.RecordBlankTurn(session.SessionId, Epoch.AddMinutes(1));

        Assert.Null(SubAgentTool.TryBuildBlankTurnRefusal(session, Epoch.AddMinutes(1)));
    }

    [Fact]
    public void ToolCallWithoutText_IsNotProducedNothing()
    {
        SubAgentTool.ReportAccumulator report = new();
        report.Add(new FunctionCallContent("call-1", "Grep", null));

        Assert.False(report.ProducedNothing);
    }

    [Fact]
    public void TextlessTurnWithoutToolCall_IsProducedNothing()
    {
        SubAgentTool.ReportAccumulator report = new();

        Assert.True(report.ProducedNothing);
    }

    private static ChatSession NewSession(string id) => new("test", new CharacterData { CharacterId = "test" })
    {
        SessionId = id,
        ParentSessionId = "parent",
    };
}
