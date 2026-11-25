using System;
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Ursa.Controls;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// Ursa <see cref="MultiComboBox"/> 的补丁壳：空态伪类 <c>:selection-empty</c> 由这里自己维护。
///
/// 上游只在<b>集合增删</b>与<b>套模板那一刻</b>更新这个伪类，另有两处漏掉，表现都是
/// 「框里已经有一枚标签了，占位文案却还亮着」：
/// <list type="number">
/// <item>整体换 <c>SelectedItems</c> 不同步（回填好的集合在模板之后才绑上来时，伪类还留着空态）；</item>
/// <item><c>OnUnloaded</c> 把集合监听摘掉后再不挂回来——切一次页签，之后的增删全都不再反映到伪类上。</item>
/// </list>
/// 所以监听归自己管：换集合、套模板、挂回界面各同步一次。
/// 模板与外观全部沿用 <c>u:MultiComboBox</c> 那一份（<see cref="StyleKeyOverride"/>）。
/// </summary>
public class MultiSelectComboBox : MultiComboBox
{
    private IList? _hooked; //当前挂过集合监听的实例

    /// <summary>模板、主题全用 <c>u:MultiComboBox</c> 那一份，本类只补行为</summary>
    protected override Type StyleKeyOverride => typeof(MultiComboBox);

    static MultiSelectComboBox()
    {
        SelectedItemsProperty.Changed.AddClassHandler<MultiSelectComboBox>((box, _) => box.Hook());
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Hook();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Hook();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Unhook();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncEmptyState();

    private void Hook()
    {
        Unhook();
        _hooked = SelectedItems;
        if (_hooked is INotifyCollectionChanged changed) changed.CollectionChanged += OnItemsChanged;
        SyncEmptyState();
    }

    private void Unhook()
    {
        if (_hooked is INotifyCollectionChanged changed) changed.CollectionChanged -= OnItemsChanged;
        _hooked = null;
    }

    private void SyncEmptyState() => PseudoClasses.Set(PC_Empty, SelectedItems is null || SelectedItems.Count == 0);
}
