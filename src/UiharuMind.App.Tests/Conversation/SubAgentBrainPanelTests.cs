using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 右栏「子代理」面板的脑分区（ADR 0082）：脑会话<b>不列条目</b>，只计数量。
/// 分区是纯函数，可不起界面线程地钉住——脑没名字、量大会刷屏，列出来是一串看不懂的。
/// </summary>
public class SubAgentBrainPanelTests
{
    [Fact]
    public void Partition_SeparatesBrainsFromDelegations()
    {
        ChatSessionMeta brain = new()
        {
            SessionId = "brain-panel-0001",
            ParentSessionId = "parent",
            SubAgentType = ESubAgentType.Brain,
        };
        ChatSessionMeta general = new()
        {
            SessionId = "brain-panel-0002",
            ParentSessionId = "parent",
            SubAgentType = ESubAgentType.General,
        };

        (List<ChatSessionMeta> delegations, int brainCount) = SubAgentListViewData.Partition([brain, general]);

        Assert.Equal(1, brainCount);
        Assert.Equal([general.SessionId], delegations.Select(x => x.SessionId));
    }

    [Fact]
    public void Partition_OnlyDelegations_GivesZeroBrains()
    {
        ChatSessionMeta general = new()
        {
            SessionId = "brain-panel-0003",
            ParentSessionId = "parent",
            SubAgentType = ESubAgentType.General,
        };

        (List<ChatSessionMeta> delegations, int brainCount) = SubAgentListViewData.Partition([general]);

        Assert.Equal(0, brainCount);
        Assert.Single(delegations);
    }

    /// <summary>探索档（ADR 0044 复活）有名字有主题，像通用委派一样进条目，不计入脑</summary>
    [Fact]
    public void Partition_ListsExplorerAsDelegation()
    {
        ChatSessionMeta explorer = new()
        {
            SessionId = "brain-panel-0004",
            ParentSessionId = "parent",
            SubAgentType = ESubAgentType.Explorer,
        };

        (List<ChatSessionMeta> delegations, int brainCount) = SubAgentListViewData.Partition([explorer]);

        Assert.Equal(0, brainCount);
        Assert.Equal([explorer.SessionId], delegations.Select(x => x.SessionId));
    }
}
