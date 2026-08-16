using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 运行态登记处。要点全在计数上：同一会话可能有多个消费方先后发起轮次
/// （界面那一轮与定时任务的无头那一轮在执行者的闸门上排队），
/// 先结束的那一个不能把状态抹成空闲——否则指示器提前熄灭、删除提前解禁。
/// </summary>
public class SessionRunRegistryTests
{
    [Fact]
    public void Idle_WhenNothingRegistered()
    {
        SessionRunRegistry registry = new();

        Assert.Equal(ESessionRunState.Idle, registry.StateOf("a"));
        Assert.False(registry.IsBusy("a"));
    }

    [Fact]
    public void Running_WhileScopeAlive()
    {
        SessionRunRegistry registry = new();

        using (registry.BeginRun("a"))
        {
            Assert.Equal(ESessionRunState.Running, registry.StateOf("a"));
            Assert.True(registry.IsBusy("a"));
        }

        Assert.Equal(ESessionRunState.Idle, registry.StateOf("a"));
    }

    [Fact]
    public void StaysRunning_UntilLastConsumerLeaves()
    {
        SessionRunRegistry registry = new();
        IDisposable ui = registry.BeginRun("a");
        IDisposable headless = registry.BeginRun("a");

        ui.Dispose();
        Assert.Equal(ESessionRunState.Running, registry.StateOf("a"));

        headless.Dispose();
        Assert.Equal(ESessionRunState.Idle, registry.StateOf("a"));
    }

    [Fact]
    public void DoubleDispose_DoesNotUndercount()
    {
        SessionRunRegistry registry = new();
        IDisposable first = registry.BeginRun("a");
        using IDisposable second = registry.BeginRun("a");

        first.Dispose();
        first.Dispose();

        Assert.Equal(ESessionRunState.Running, registry.StateOf("a"));
    }

    [Fact]
    public void AwaitingApproval_OutranksRunning()
    {
        SessionRunRegistry registry = new();

        using IDisposable run = registry.BeginRun("a");
        using (registry.BeginApprovalWait("a"))
        {
            Assert.Equal(ESessionRunState.AwaitingApproval, registry.StateOf("a"));
        }

        //审批回应完了,那一轮还在跑
        Assert.Equal(ESessionRunState.Running, registry.StateOf("a"));
    }

    [Fact]
    public void SessionsDoNotLeakIntoEachOther()
    {
        SessionRunRegistry registry = new();

        using IDisposable run = registry.BeginRun("a");

        Assert.Equal(ESessionRunState.Idle, registry.StateOf("b"));
    }

    [Fact]
    public void EmptyId_IsAlwaysIdle()
    {
        SessionRunRegistry registry = new();

        //首轮发送前的新会话还没有标识,登记要是空操作而不是把 "" 记成在跑
        using IDisposable scope = registry.BeginRun(null);

        Assert.Equal(ESessionRunState.Idle, registry.StateOf(null));
        Assert.False(registry.IsBusy(string.Empty));
    }

    [Fact]
    public void StateChanged_FiresOnBothEdges()
    {
        SessionRunRegistry registry = new();
        List<string> notified = new();
        registry.StateChanged += notified.Add;

        registry.BeginRun("a").Dispose();

        Assert.Equal(["a", "a"], notified);
    }
}
