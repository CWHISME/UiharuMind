using SharpHook.Data;
using UiharuMind.Core.Input;
using Xunit;

namespace UiharuMind.Core.Tests.Input;

/// <summary>
/// 快捷键匹配是整个全局热键链路上唯一的判定点，它的两条语义——左右修饰键等价、修饰键集合排他——
/// 都只有在真机上误触发时才会被察觉，故在此钉死。
/// </summary>
public class ShortcutMatchingTests
{
    [Fact]
    public void Matches_TreatsLeftAndRightModifierAsSame()
    {
        var combination = Create(KeyCode.VcA, KeyCode.VcLeftControl);

        Assert.True(combination.Matches(KeyCode.VcA, EModifierKeys.RightCtrl.Fold()));
        Assert.True(combination.Matches(KeyCode.VcA, EModifierKeys.LeftCtrl.Fold()));
    }

    [Fact]
    public void Matches_RejectsExtraModifier()
    {
        var combination = Create(KeyCode.VcA, KeyCode.VcLeftControl);

        Assert.False(combination.Matches(KeyCode.VcA, (EModifierKeys.LeftCtrl | EModifierKeys.LeftShift).Fold()));
    }

    [Fact]
    public void Matches_RejectsMissingModifier()
    {
        var combination = Create(KeyCode.VcA, KeyCode.VcLeftControl, KeyCode.VcLeftShift);

        Assert.False(combination.Matches(KeyCode.VcA, EModifierKeys.LeftCtrl.Fold()));
    }

    [Fact]
    public void Matches_RequiresNoModifierWhenNoneRegistered()
    {
        var combination = Create(KeyCode.VcF1);

        Assert.True(combination.Matches(KeyCode.VcF1, EModifierKeys.None));
        Assert.False(combination.Matches(KeyCode.VcF1, EModifierKeys.LeftAlt.Fold()));
    }

    /// 排他匹配的直接收益：前缀相同的两条快捷键可以共存，各走各的
    [Fact]
    public void TryTrigger_PicksTheExactCombination()
    {
        var registry = new ShortcutRegistry();
        var triggered = string.Empty;
        registry.Register(Create(KeyCode.VcA, () => triggered = "ctrl", KeyCode.VcLeftControl));
        registry.Register(Create(KeyCode.VcA, () => triggered = "ctrl+shift", KeyCode.VcLeftControl,
            KeyCode.VcLeftShift));

        Assert.True(registry.TryTrigger(KeyCode.VcA, EModifierKeys.RightCtrl | EModifierKeys.LeftShift));
        Assert.Equal("ctrl+shift", triggered);

        Assert.True(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));
        Assert.Equal("ctrl", triggered);
    }

    [Fact]
    public void TryTrigger_IgnoresUnregisteredCombination()
    {
        var registry = new ShortcutRegistry();
        var count = 0;
        var combination = Create(KeyCode.VcA, () => count++, KeyCode.VcLeftControl);
        registry.Register(combination);
        registry.Unregister(combination);

        Assert.False(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));
        Assert.Equal(0, count);
    }

    [Fact]
    public void Suspend_StopsDispatchUntilScopeDisposed()
    {
        var registry = new ShortcutRegistry();
        var count = 0;
        registry.Register(Create(KeyCode.VcA, () => count++, KeyCode.VcLeftControl));

        var outer = registry.Suspend();
        var inner = registry.Suspend();
        Assert.False(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));

        inner.Dispose();
        Assert.False(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));

        outer.Dispose();
        Assert.True(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));
        Assert.Equal(1, count);
    }

    /// 调用方普遍写成 scope?.Dispose() 后置 null，重复释放不得把挂起计数减穿
    [Fact]
    public void Suspend_DisposeIsIdempotent()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Create(KeyCode.VcA, () => { }, KeyCode.VcLeftControl));

        var first = registry.Suspend();
        var second = registry.Suspend();
        first.Dispose();
        first.Dispose();

        Assert.False(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));
        second.Dispose();
        Assert.True(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));
    }

    /// 回调抛异常不得打断钩子线程，否则一条坏快捷键会拖垮全局监听
    [Fact]
    public void TryTrigger_SwallowsCallbackException()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Create(KeyCode.VcA, () => throw new InvalidOperationException("boom"),
            KeyCode.VcLeftControl));

        Assert.True(registry.TryTrigger(KeyCode.VcA, EModifierKeys.LeftCtrl));
    }

    private static KeyCombinationData Create(KeyCode mainKey, params KeyCode[] modifiers) =>
        Create(mainKey, () => { }, modifiers);

    private static KeyCombinationData Create(KeyCode mainKey, Action onTrigger, params KeyCode[] modifiers) =>
        new(mainKey, onTrigger, modifiers.ToList(), "test");
}
