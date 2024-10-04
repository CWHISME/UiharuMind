using System;
using SharpHook.Native;
using UiharuMind.Core.Input;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

public class QuickWindowBase : UiharuWindowBase
{
    public override void Awake()
    {
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

    public void PlayOpenAnimation()
    {
        UiAnimationUtils.PlayAlphaTransitionAnimation(this, true, null);
    }

    public void CloseByAnimation()
    {
        UiAnimationUtils.PlayAlphaTransitionAnimation(this, false, SafeClose);
    }
}