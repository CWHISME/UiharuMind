using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 左栏父会话行的「名下子代理在跑/卡审批」判据：后台委派由 <c>PendingWork</c> 覆盖，
/// 这里钉的是<b>前台私聊轮</b>——它不登记后台委派表，只能从活动会话反查
/// （<see cref="SessionManager.HasRunningSubSessions"/> / <see cref="SessionManager.HasAwaitingApprovalSubSessions"/>）
/// </summary>
public class SessionManagerSubSessionRunStateTests
{
    [Fact]
    public void NoSubSessions_OrIdle_IsQuiet()
    {
        string parent = "runstate-parent-idle";
        SessionManager.Instance.Add(new ChatSession { SessionId = parent });
        try
        {
            Assert.False(SessionManager.Instance.HasRunningSubSessions(parent));
            Assert.False(SessionManager.Instance.HasAwaitingApprovalSubSessions(parent));
        }
        finally
        {
            SessionManager.Instance.Delete(parent);
        }
    }

    [Fact]
    public void ForegroundTurn_Running_ShowsOnParent()
    {
        string parent = "runstate-parent-run";
        string sub = "runstate-sub-run";
        SessionManager.Instance.Add(new ChatSession { SessionId = parent });
        SessionManager.Instance.Add(new ChatSession { SessionId = sub, ParentSessionId = parent });
        try
        {
            using (SessionManager.Instance.Running.BeginRun(sub))
            {
                Assert.True(SessionManager.Instance.HasRunningSubSessions(parent));
                Assert.False(SessionManager.Instance.HasAwaitingApprovalSubSessions(parent));
            }

            Assert.False(SessionManager.Instance.HasRunningSubSessions(parent));
        }
        finally
        {
            SessionManager.Instance.Delete(sub);
            SessionManager.Instance.Delete(parent);
        }
    }

    [Fact]
    public void ForegroundTurn_AwaitingApproval_ShowsOrangeOnParent()
    {
        string parent = "runstate-parent-approval";
        string sub = "runstate-sub-approval";
        SessionManager.Instance.Add(new ChatSession { SessionId = parent });
        SessionManager.Instance.Add(new ChatSession { SessionId = sub, ParentSessionId = parent });
        try
        {
            using (SessionManager.Instance.Running.BeginRun(sub))
            using (SessionManager.Instance.Running.BeginApprovalWait(sub))
            {
                Assert.True(SessionManager.Instance.HasRunningSubSessions(parent));
                Assert.True(SessionManager.Instance.HasAwaitingApprovalSubSessions(parent));
            }

            Assert.False(SessionManager.Instance.HasAwaitingApprovalSubSessions(parent));
        }
        finally
        {
            SessionManager.Instance.Delete(sub);
            SessionManager.Instance.Delete(parent);
        }
    }

    [Fact]
    public void OtherParentsRunning_DoNotCount()
    {
        string parent = "runstate-parent-a";
        string other = "runstate-parent-b";
        string sub = "runstate-sub-b";
        SessionManager.Instance.Add(new ChatSession { SessionId = parent });
        SessionManager.Instance.Add(new ChatSession { SessionId = other });
        SessionManager.Instance.Add(new ChatSession { SessionId = sub, ParentSessionId = other });
        try
        {
            using (SessionManager.Instance.Running.BeginRun(sub))
            {
                Assert.False(SessionManager.Instance.HasRunningSubSessions(parent));
                Assert.True(SessionManager.Instance.HasRunningSubSessions(other));
            }
        }
        finally
        {
            SessionManager.Instance.Delete(sub);
            SessionManager.Instance.Delete(other);
            SessionManager.Instance.Delete(parent);
        }
    }

    [Fact]
    public void NullOrEmptyParent_IsSafe()
    {
        Assert.False(SessionManager.Instance.HasRunningSubSessions(null));
        Assert.False(SessionManager.Instance.HasRunningSubSessions(string.Empty));
        Assert.False(SessionManager.Instance.HasAwaitingApprovalSubSessions(null));
        Assert.False(SessionManager.Instance.HasAwaitingApprovalSubSessions(string.Empty));
    }
}
