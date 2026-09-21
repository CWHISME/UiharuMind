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
using Avalonia.Threading;
using SharpHook.Data;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Input;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 在鼠标位置弹出浮动快捷按钮工具栏之类的
/// </summary>
public class QuickFloatingWindowBase : QuickWindowBase
{
    protected override bool IsAllowFocusOnOpen => false;

    public override void Awake()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        this.SetSimpledecorationPureWindow();
        CanResize = true;
        ShowActivated = false;
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        InputManager.Instance.EventOnKeyDown += OnKeyDown;
        InputManager.Instance.EventOnMouseWheel += OnMouseWheel;
        BindMouseClickCloseEvent();
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 这类浮窗是「复制完马上要看到」的提示，必须压在钉图窗之上，否则被贴图整个盖掉
        OverlayWindowService.ApplyNativeWindowLevel(this, EOverlayWindowLevel.FloatingTool);
        Dispatcher.UIThread.Post(SetWindowPosition, DispatcherPriority.Loaded);
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
