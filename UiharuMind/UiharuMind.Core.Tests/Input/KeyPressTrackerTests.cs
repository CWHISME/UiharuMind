using SharpHook.Data;
using UiharuMind.Core.Input;
using Xunit;

namespace UiharuMind.Core.Tests.Input;

/// <summary>
/// 这里守的是历史上最顽固的一类故障：修饰键的 release 被系统吞掉后残留在状态里，
/// 导致之后每一次按主键都被当成组合键重复触发。修法是拿事件自带的权威位集逐次对账，
/// 以下用例即是对账行为的固化。
/// </summary>
public class KeyPressTrackerTests
{
    [Fact]
    public void TryBeginPress_ReportsRepeatForHeldKey()
    {
        var tracker = new KeyPressTracker();

        Assert.True(tracker.TryBeginPress(Press(KeyCode.VcA)));
        Assert.False(tracker.TryBeginPress(Press(KeyCode.VcA)));

        tracker.EndPress(Release(KeyCode.VcA));
        Assert.True(tracker.TryBeginPress(Press(KeyCode.VcA)));
    }

    [Fact]
    public void TryBeginPress_ReportsRepeatForHeldModifier()
    {
        var tracker = new KeyPressTracker();

        Assert.True(tracker.TryBeginPress(Press(KeyCode.VcLeftControl, EModifierKeys.LeftCtrl)));
        Assert.False(tracker.TryBeginPress(Press(KeyCode.VcLeftControl, EModifierKeys.LeftCtrl)));
    }

    /// 丢掉 release 的场景：按下 Ctrl 后其 release 事件从未到达，
    /// 但下一个事件的权威位集里已经没有 Ctrl，残留必须当场被清掉
    [Fact]
    public void LostModifierRelease_IsReconciledByNextEvent()
    {
        var tracker = new KeyPressTracker();
        tracker.TryBeginPress(Press(KeyCode.VcLeftControl, EModifierKeys.LeftCtrl));
        Assert.True(tracker.IsPressed(KeyCode.VcLeftControl));

        // Ctrl 的 release 丢失，用户直接按下 A，此时系统给出的位集里已无 Ctrl
        tracker.TryBeginPress(Press(KeyCode.VcA));

        Assert.False(tracker.IsPressed(KeyCode.VcLeftControl));
        Assert.Equal(EModifierKeys.None, tracker.Modifiers);
    }

    [Fact]
    public void SyncModifiers_DropsStaleModifierButKeepsNormalKey()
    {
        var tracker = new KeyPressTracker();
        tracker.TryBeginPress(Press(KeyCode.VcLeftShift, EModifierKeys.LeftShift));
        tracker.TryBeginPress(Press(KeyCode.VcA, EModifierKeys.LeftShift));

        tracker.SyncModifiers(EModifierKeys.None);

        Assert.False(tracker.IsPressed(KeyCode.VcLeftShift));
        Assert.True(tracker.IsPressed(KeyCode.VcA));
    }

    /// 正在按下的那个修饰键自身不参与对账，否则它会把刚到达的按下事件自己抹掉
    [Fact]
    public void TryBeginPress_KeepsTheModifierBeingPressed()
    {
        var tracker = new KeyPressTracker();

        tracker.TryBeginPress(Press(KeyCode.VcLeftAlt, EModifierKeys.LeftAlt));

        Assert.True(tracker.IsPressed(KeyCode.VcLeftAlt));
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var tracker = new KeyPressTracker();
        tracker.TryBeginPress(Press(KeyCode.VcLeftMeta, EModifierKeys.LeftMeta));
        tracker.TryBeginPress(Press(KeyCode.VcA, EModifierKeys.LeftMeta));

        tracker.Reset();

        Assert.False(tracker.IsPressed(KeyCode.VcLeftMeta));
        Assert.False(tracker.IsPressed(KeyCode.VcA));
        Assert.Equal(EModifierKeys.None, tracker.Modifiers);
    }

    private static KeyEventInfo Press(KeyCode keyCode, EModifierKeys modifiers = EModifierKeys.None) =>
        new(keyCode, modifiers, IsSimulated: false);

    private static KeyEventInfo Release(KeyCode keyCode, EModifierKeys modifiers = EModifierKeys.None) =>
        new(keyCode, modifiers, IsSimulated: false);
}
