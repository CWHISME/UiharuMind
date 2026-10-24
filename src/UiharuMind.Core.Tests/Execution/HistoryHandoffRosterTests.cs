using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 记忆压缩里的委派段。
///
/// 不逐条列子代理——<c>ExCall</c> 调 <c>chatsession</c> 扩展的 <c>list</c> 命令能实时列出状态，
/// 比压缩时固化的快照更新也更省篇幅。这里只报数量、把仍在跑的点名出来、再指路怎么查怎么续。
/// </summary>
public class HistoryHandoffRosterTests
{
    private static ChatSessionMeta Sub(string id, string title) =>
        new()
        {
            SessionId = id,
            Title = title,
            ParentSessionId = "parent",
        };

    private static AITool Named(string name) => AIFunctionFactory.Create(() => string.Empty, name);

    private static Func<string, ESessionRunState> IdleAll => _ => ESessionRunState.Idle;

    [Fact]
    public void NoDelegations_ProducesNothing()
    {
        //没派过活时整段不出现,不占记忆压缩的篇幅
        Assert.Equal(string.Empty, HistoryHandoff.BuildSubSessionRoster([], null));
    }

    [Fact]
    public void Roster_ReportsCountAndPointsToTheExtension()
    {
        string roster = HistoryHandoff.BuildSubSessionRoster(
            [Sub("abc123", "查配置加载"), Sub("def456", "审一遍 diff")],
            [Named(ExtensionCalls.CallName)], IdleAll);

        Assert.Contains("2 位子代理。", roster);
        Assert.Contains(ExtensionCalls.CallName, roster); //指路走扩展元工具
        Assert.Contains(BuiltinExtensions.ChatSession, roster); //扩展名
        Assert.Contains(BuiltinExtensions.ChatSessionList, roster); //命令名
        Assert.Contains(SubAgentTool.MessageToolName, roster); //续聊:编号作为 to 传给 SendMessage
        Assert.Contains(SubAgentTool.ToolName, roster); //别新开:会丢掉子代理攒下的上下文
        Assert.DoesNotContain("abc123", roster); //不逐条列,状态与编号由扩展现查
        Assert.DoesNotContain("查配置加载", roster);
    }

    [Fact]
    public void Roster_ListsTheOnesStillRunning()
    {
        ChatSessionMeta running = Sub("run123", "查配置加载");
        ChatSessionMeta idle = Sub("idle456", "审一遍 diff");

        string roster = HistoryHandoff.BuildSubSessionRoster(
            [running, idle],
            [Named(ExtensionCalls.CallName)],
            id => id == running.SessionId ? ESessionRunState.Running : ESessionRunState.Idle);

        Assert.Contains("2 位子代理，其中 1 位还在工作中", roster);
        Assert.Contains($"-  · 《查配置加载》[{SubSessionIdAlias.Short(running.SessionId)}] · 工作中", roster);
        Assert.DoesNotContain("idle456", roster); //空闲的不占篇幅
        Assert.DoesNotContain("审一遍 diff", roster);
    }

    [Fact]
    public void Roster_WithoutExCall_StillListsRunningOnes_ButGivesNoDirection()
    {
        //运行中列表不依赖扩展(从运行登记现读),该给的照给;没有扩展元工具就不指路,免得模型去调查不了的工具
        ChatSessionMeta running = Sub("run123", "查配置加载");

        string roster = HistoryHandoff.BuildSubSessionRoster([running], null,
            id => id == running.SessionId ? ESessionRunState.AwaitingApproval : ESessionRunState.Idle);

        Assert.Contains("1 位子代理，其中 1 位还在工作中", roster);
        Assert.Contains("· 等审批", roster);
        Assert.DoesNotContain(ExtensionCalls.CallName, roster);
        Assert.DoesNotContain(BuiltinExtensions.ChatSession, roster);
    }
}
