using System;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 选会话模型的下拉框，项是 <see cref="SessionModelOption"/>。会话模型面板与建群挑成员共用
/// </summary>
public partial class SessionModelComboBox : ComboBox
{
    /// <summary>加载布局</summary>
    public SessionModelComboBox()
    {
        InitializeComponent();
    }

    /// <summary>沿用 ComboBox 的主题样式</summary>
    protected override Type StyleKeyOverride => typeof(ComboBox);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedItemProperty)
            ToolTip.SetTip(this, (SelectedItem as SessionModelOption)?.DisplayName);
    }
}
