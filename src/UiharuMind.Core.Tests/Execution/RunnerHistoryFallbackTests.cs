using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 执行者给出的历史：绑定着按绑定读，读不回来只在挂接的就是同一个会话时回落；绑定被清掉
/// （装配迁移失败）回落挂接着的会话。
///
/// 这几条给空表的后果不只是「少画几条」：界面会把窗口起点判成 0、挂上「已到会话开头」，
/// 而续窗的触发条件变成假，滚到顶再也续不出来，只能重开会话
/// </summary>
public class RunnerHistoryFallbackTests
{
    private static ChatSession SessionWith(string sessionId, string text)
    {
        ChatSession session = new() { SessionId = sessionId, IsTransient = true };
        session.History.Add(new ChatMessage(ChatRole.User, text));
        return session;
    }

    /// <summary>绑定读得回来：用绑定的那一份</summary>
    [Fact]
    public void BoundAndLoadable_UsesTheBoundSession()
    {
        ChatSession attached = SessionWith("a", "挂接的");
        ChatSession bound = SessionWith("a", "绑定的");

        IReadOnlyList<ChatMessage> history = HarnessCharacterRunner.ResolveHistory("a", attached, _ => bound);

        Assert.Same(bound.History[0], history[0]);
    }

    /// <summary>绑定读不回来、挂接的就是同一个会话：回落它，别给空表</summary>
    [Fact]
    public void BoundButUnloadable_SameSession_FallsBackToTheAttached()
    {
        ChatSession attached = SessionWith("a", "挂接的");

        IReadOnlyList<ChatMessage> history = HarnessCharacterRunner.ResolveHistory("a", attached, _ => null);

        Assert.Same(attached.History[0], history[0]);
    }

    /// <summary>绑定读不回来、挂接的是<b>别的</b>会话：宁给空表，也不能把别人的历史递出去</summary>
    [Fact]
    public void BoundButUnloadable_OtherSession_HandsNothing()
    {
        ChatSession attached = SessionWith("b", "别人的");

        Assert.Empty(HarnessCharacterRunner.ResolveHistory("a", attached, _ => null));
    }

    /// <summary>绑定被清掉（装配迁移失败）：回落挂接着的会话</summary>
    [Fact]
    public void Unbound_FallsBackToTheAttached()
    {
        ChatSession attached = SessionWith("a", "挂接的");

        IReadOnlyList<ChatMessage> history = HarnessCharacterRunner.ResolveHistory(null, attached, _ => null);

        Assert.Same(attached.History[0], history[0]);
    }

    /// <summary>什么都没挂：空表（这时候的空是真的空）</summary>
    [Fact]
    public void NothingAttached_HandsNothing()
    {
        Assert.Empty(HarnessCharacterRunner.ResolveHistory(null, null, _ => null));
        Assert.Empty(HarnessCharacterRunner.ResolveHistory("a", null, _ => null));
    }
}
