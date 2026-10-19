using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 「子代理已跑完、报告已落盘但主代理还没读」这一段的待交还登记与清除：
/// 派活者空闲时报告直接落盘（Written），从用户角度同样是「跑完了、主代理还没理」，
/// 输入区上方那行要一直显示到报告被读过（不再停在历史末尾）为止（ADR 0062 实测补的档）
/// </summary>
public class BackgroundSubAgentHandoffTrackingTests
{
    [Fact]
    public void ReportStillUnread_TrueWhileReportIsAtTail()
    {
        ChatSession parent = NewParent("unread-at-tail");
        parent.History.Add(Report("unread-at-tail-sub"));

        Assert.True(BackgroundSubAgentDispatcher.ReportStillUnread(parent.History, "unread-at-tail-sub"));
    }

    [Fact]
    public void ReportStillUnread_FalseOnceSomethingFollows()
    {
        ChatSession parent = NewParent("unread-followed");
        parent.History.Add(Report("unread-followed-sub"));
        parent.History.Add(new ChatMessage(ChatRole.Assistant, "主代理读完了"));

        Assert.False(BackgroundSubAgentDispatcher.ReportStillUnread(parent.History, "unread-followed-sub"));
    }

    [Fact]
    public void ReportStillUnread_TrueWhenNotWrittenYet()
    {
        // 交回进行中（还没落盘）：历史里找不到，按未读保留——清理交给交回流程自己收口
        Assert.True(BackgroundSubAgentDispatcher.ReportStillUnread([], "not-written-sub"));
    }

    [Fact]
    public void Refresh_KeepsHandoffWhileReportIsAtTail()
    {
        string parentId = "refresh-keep";
        string subId = "refresh-keep-sub";
        ChatSession parent = NewParent(parentId);
        parent.History.Add(Report(subId));
        BackgroundSubAgentDispatcher.MarkHandoffQueued(parentId, subId);

        BackgroundSubAgentDispatcher.RefreshHandoffQueued(parentId);

        Assert.True(BackgroundSubAgentDispatcher.IsHandoffQueued(parentId, subId));
    }

    [Fact]
    public void Refresh_ClearsHandoffOnceReportIsRead()
    {
        string parentId = "refresh-clear";
        string subId = "refresh-clear-sub";
        ChatSession parent = NewParent(parentId);
        parent.History.Add(Report(subId));
        parent.History.Add(new ChatMessage(ChatRole.Assistant, "主代理读完了"));
        BackgroundSubAgentDispatcher.MarkHandoffQueued(parentId, subId);

        BackgroundSubAgentDispatcher.RefreshHandoffQueued(parentId);

        Assert.False(BackgroundSubAgentDispatcher.IsHandoffQueued(parentId, subId));
    }

    [Fact]
    public void Refresh_ClearsHandoffWhenParentMissing()
    {
        string parentId = "refresh-parent-missing";
        BackgroundSubAgentDispatcher.MarkHandoffQueued(parentId, "any-sub");

        BackgroundSubAgentDispatcher.RefreshHandoffQueued(parentId);

        Assert.False(BackgroundSubAgentDispatcher.IsHandoffQueued(parentId, "any-sub"));
    }

    private static ChatSession NewParent(string sessionId)
    {
        ChatSession parent = new() { SessionId = sessionId };
        SessionManager.Instance.Add(parent);
        return parent;
    }

    private static ChatMessage Report(string subSessionId) =>
        new(ChatRole.User, "来自 子代理 [sub-session: x]，回你之前发的「…」：\n\n结论")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = subSessionId,
            },
        };
}
