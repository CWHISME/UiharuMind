using SharpHook.Data;
using UiharuMind.Core.Input;
using Xunit;

namespace UiharuMind.Core.Tests.Input;

public class ModifierKeysTests
{
    [Theory]
    [InlineData(KeyCode.VcLeftControl, EModifierKeys.LeftCtrl)]
    [InlineData(KeyCode.VcRightMeta, EModifierKeys.RightMeta)]
    [InlineData(KeyCode.VcA, EModifierKeys.None)]
    public void ToModifier_MapsKeyCode(KeyCode keyCode, EModifierKeys expected)
    {
        Assert.Equal(expected, keyCode.ToModifier());
    }

    /// 折叠的意义在于让「左 Ctrl 注册、右 Ctrl 按下」判等成立
    [Fact]
    public void Fold_MakesBothSidesEqual()
    {
        Assert.Equal(EModifierKeys.LeftCtrl.Fold(), EModifierKeys.RightCtrl.Fold());
        Assert.NotEqual(EModifierKeys.LeftCtrl.Fold(), EModifierKeys.LeftAlt.Fold());
    }

    [Fact]
    public void Fold_KeepsDistinctGroups()
    {
        var folded = (EModifierKeys.RightShift | EModifierKeys.LeftMeta).Fold();

        Assert.Equal(EModifierKeys.Shift | EModifierKeys.Meta, folded);
    }

    [Fact]
    public void ToModifiers_IgnoresNonModifierAndNull()
    {
        Assert.Equal(EModifierKeys.None, ((IEnumerable<KeyCode>?)null).ToModifiers());
        Assert.Equal(EModifierKeys.LeftAlt, new[] { KeyCode.VcLeftAlt, KeyCode.VcA }.ToModifiers());
    }
}
