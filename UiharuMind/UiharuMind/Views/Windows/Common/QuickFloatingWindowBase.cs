/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using SharpHook.Native;
using UiharuMind.Core.Input;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

/// <summary>
/// 在鼠标位置弹出浮动快捷按钮工具栏之类的
/// </summary>
public class QuickFloatingWindowBase : QuickWindowBase
{
    public override void Awake()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        this.SetSimpledecorationPureWindow();
        this.CanResize = true;
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        InputManager.Instance.EventOnKeyDown += OnKeyDown;
        InputManager.Instance.EventOnMouseWheel += OnMouseWheel;
        BindMouseClickCloseEvent();

        ShowActivated = false;
        SetWindowPosition();
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        InputManager.Instance.EventOnKeyDown -= OnKeyDown;
        InputManager.Instance.EventOnMouseWheel -= OnMouseWheel;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        // if (MainMenu.IsVisible) return;
        PlayAnimation(true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        // if (SubMenuComboBox.IsDropDownOpen) return;
        PlayAnimation(false);
    }

    protected virtual void PlayAnimation(bool isShowed, Action? onCompleted = null)
    {
        // UiAnimationUtils.PlayRightToLeftTransitionAnimation(MainMenu, isShowed, onCompleted);
        // if (!isShowed) SafeClose();
        onCompleted?.Invoke();
    }
    
    protected virtual void SetWindowPosition()
    {
        this.SetWindowToMousePosition(HorizontalAlignment.Right, offsetX: 15, offsetY: -15);
    }

    private void OnMouseWheel(MouseWheelEventData obj)
    {
        SafeClose();
    }

    private void OnKeyDown(KeyCode obj)
    {
        SafeClose();
    }
}