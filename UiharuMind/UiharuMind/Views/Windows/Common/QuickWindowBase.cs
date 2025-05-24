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
using SharpHook.Native;
using UiharuMind.Core.Input;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

public class QuickWindowBase : UiharuWindowBase
{

    public override void Awake()
    {
        base.Awake();
        this.SetSimpledecorationWindow();
    }

    protected override void OnOpened(EventArgs e)
    {
        PlayOpenAnimation();
        base.OnOpened(e);
    }

    /// <summary>
    /// 绑定鼠标点击关闭事件，点击鼠标时若不在当前界面，则关闭当前界面
    /// </summary>
    protected void BindMouseClickCloseEvent()
    {
        InputManager.Instance.EventOnMouseClicked += OnMouseClicked;
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        InputManager.Instance.EventOnMouseClicked -= OnMouseClicked;
    }

    protected void OnMouseClicked(MouseEventData obj)
    {
        // if (SubMenuComboBox.IsFocused) return;
        this.CheckMouseOutsideWindow(CloseByAnimation);
    }

    public void PlayOpenAnimation(Action? action = null)
    {
        UiAnimationUtils.PlayAlphaTransitionAnimation(this, true, action);
    }

    public void CloseByAnimation()
    {
        UiAnimationUtils.PlayAlphaTransitionAnimation(this, false, SafeClose);
    }
}