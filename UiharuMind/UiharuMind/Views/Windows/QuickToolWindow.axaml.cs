using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using SharpHook.Native;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 当复制操作发生后，显示在复制位置的工具
/// </summary>
public partial class QuickToolWindow : QuickWindowBase
{
    public QuickToolWindow()
    {
        InitializeComponent();

        SizeToContent = SizeToContent.WidthAndHeight;
        this.SetSimpledecorationPureWindow();

        // SubMenuComboBox.SelectionChanged += OnSubMenuComboBoxSelectionChanged;
    }

    private void OnSubMenuComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // CloseSelf();
        if (sender == null) return;
        var comboBox = (ComboBox)sender;
        var newItem = comboBox.SelectedItem;
        if (newItem != null)
        {
            comboBox.SelectedItem = null;
            PlayAnimation(false, SafeClose);
        }

        // e.Handled = true;
    }


    protected override void OnPreShow()
    {
        InputManager.Instance.EventOnKeyDown += OnKeyDown;
        InputManager.Instance.EventOnMouseClicked += OnMouseClicked;
        InputManager.Instance.EventOnMouseWheel += OnMouseWheel;

        ShowActivated = false;
        this.SetWindowToMousePosition(HorizontalAlignment.Right, offsetX: 10, offsetY: -15);
    }

    protected override void OnPreClose()
    {
        InputManager.Instance.EventOnKeyDown -= OnKeyDown;
        InputManager.Instance.EventOnMouseClicked -= OnMouseClicked;
        InputManager.Instance.EventOnMouseWheel -= OnMouseWheel;
    }

    public void SetAnswerString(string text)
    {
        Log.Debug("Set answer string: " + text);
    }

    private void OnMainButtonClock(object? sender, RoutedEventArgs e)
    {
        UIManager.ShowWindow<QuickChatResultWindow>();
        PlayAnimation(false, SafeClose);
    }

    private void OnMouseClicked(MouseEventData obj)
    {
        // if (SubMenuComboBox.IsFocused) return;
        this.CheckMouseOutsideWindow(SafeClose);
    }

    private void OnMouseWheel(MouseWheelEventData obj)
    {
        SafeClose();
    }

    private void OnKeyDown(KeyCode obj)
    {
        SafeClose();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        if (MainMenu.IsVisible) return;
        PlayAnimation(true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        // if (SubMenuComboBox.IsDropDownOpen) return;
        PlayAnimation(false);
    }

    private void PlayAnimation(bool isShowed, Action? onCompleted = null)
    {
        UiAnimationUtils.PlayRightToLeftTransitionAnimation(MainMenu, isShowed, onCompleted);
    }
}