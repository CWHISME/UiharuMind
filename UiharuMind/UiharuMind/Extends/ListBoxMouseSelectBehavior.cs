using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Extends;

public class ListBoxMouseSelectBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBoxMouseSelectBehavior, ListBox, bool>(
            "IsEnabled", 
            false, 
            false, 
            BindingMode.OneWay, 
            null, 
            OnIsEnabledChanged);

    public static bool GetIsEnabled(ListBox listBox) => listBox.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ListBox listBox, bool value) => listBox.SetValue(IsEnabledProperty, value);

    private static bool OnIsEnabledChanged(AvaloniaObject d, bool isEnabled)
    {
        if (d is ListBox listBox)
        {
            if (isEnabled)
            {
                var behavior = new ListBoxMouseSelectBehavior(listBox);
                behavior.Attach();
            }
            else
            {
                // 如果禁用，移除事件处理程序
                // listBox.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
                // listBox.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
                // listBox.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
                Log.Error("not allowed to disable this behavior");
            }
        }
        return isEnabled;
    }

    private ListBox _listBox;
    private bool _isSelecting;
    private Point _startPosition;

    private ListBoxMouseSelectBehavior(ListBox listBox)
    {
        _listBox = listBox;
    }

    private void Attach()
    {
        _listBox.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _listBox.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        _listBox.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        _isSelecting = true;
        _startPosition = e.GetPosition(_listBox);
        SelectItemAtPosition(_startPosition);
    }

    private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        _isSelecting = false;
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (_isSelecting)
        {
            var currentPosition = e.GetPosition(_listBox);
            SelectItemAtPosition(currentPosition);
        }
    }

    private void SelectItemAtPosition(Point position)
    {
        var item = _listBox.GetVisualAt(position)?.GetSelfAndVisualAncestors()
            .OfType<ListBoxItem>()
            .FirstOrDefault();

        if (item != null)
        {
            // 取消之前的选择
            foreach (var listBoxItem in _listBox.GetLogicalChildren().OfType<ListBoxItem>())
            {
                listBoxItem.IsSelected = false;
            }

            // 选择当前项
            item.IsSelected = true;
        }
    }
}