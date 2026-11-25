/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 一档子代理模型的选择器：外观与绑定只写在这里，页面里三行各给一个 <see cref="SubAgentModelSlot"/>。
///
/// <b>不接管 DataContext</b>：控件里的绑定走 <c>RelativeSource AncestorType</c>，
/// 页面那份 ViewData 照常继承下来——否则宿主的 <c>Slot="{Binding …}"</c> 会和自己的
/// DataContext 赋值互相打架（祖先绑定的源就是子控件的 DataContext）。
/// </summary>
public partial class SubAgentModelPicker : UserControl
{
    /// <summary>这一档的已选与落盘都在它身上。null 时控件空白</summary>
    public static readonly StyledProperty<SubAgentModelSlot?> SlotProperty =
        AvaloniaProperty.Register<SubAgentModelPicker, SubAgentModelSlot?>(nameof(Slot));

    public SubAgentModelPicker()
    {
        InitializeComponent();
    }

    /// <summary>这一档的模型槽</summary>
    public SubAgentModelSlot? Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }
}
