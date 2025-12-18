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
using Avalonia.Threading;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

public abstract class UiharuWindowBase : Window
{
    public event Action? OnPreCloseEvent;

    protected int StartWidth;
    protected int StartHeight;

    /// <summary>
    /// 是否不关闭，重复复用
    /// </summary>
    public virtual bool IsCacheWindow => true;

    protected UiharuWindowBase()
    {
        Activated += OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        // UIManager.ClosingWindowSet.Remove(this);
    }

    public void RequestShow(bool isFirstShow = false, bool isActivate = true)
    {
        // UIManager.ClosingWindowSet.Add(this);
        // if (isActivate && IsAllowFocusOnOpen) ShowActivated = true;
        OnPreShow();
        if (isFirstShow)
        {
            if (IconUtils.DefaultAppIcon != null) Icon = new WindowIcon(IconUtils.DefaultAppIcon);
            OnInitWindowPosition();
            StartWidth = (int)Width;
            StartHeight = (int)Height;
            Show();
        }
        else
        {
            this.WindowState = WindowState.Normal;
            Dispatcher.UIThread.Post(() =>
            {
                Show();
                if (isActivate && IsAllowFocusOnOpen) this.Activate();
            }, DispatcherPriority.ApplicationIdle);
        }
        // 
        // else
        // {
        //     this.WindowState = WindowState.Normal;
        // Dispatcher.UIThread.Post(Show, DispatcherPriority.ApplicationIdle);
        // }
    }

    protected virtual bool IsAllowFocusOnOpen { get; set; } = true;

    public virtual void Awake()
    {
    }

    protected virtual void OnInitWindowPosition()
    {
        this.SetScreenCenterPosition();
    }

    protected virtual void OnPreShow()
    {
    }

    protected virtual void OnPreClose()
    {
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (IsCacheWindow)
        {
            e.Cancel = true;
            base.OnClosing(e);
            // UIManager.ClosingWindowSet.Add(this);
            App.DummyWindow.Activate();
            SafeClose();
        }
        else
        {
            OnPreCloseEvent?.Invoke();
            OnPreClose();
            base.OnClosing(e);
        }
    }

    protected virtual void SafeClose()
    {
        // UIManager.IsClosing = true;
        // UIManager.ClosingWindowSet.Add(this);
        OnPreCloseEvent?.Invoke();
        OnPreClose();
        Dispatcher.UIThread.Post(Hide);
        // Dispatcher.UIThread.Post(() =>
        // {
        //     if (ShowActivated || IsActive) App.DummyWindow.Activate();
        //     Hide();
        //     // UIManager.IsClosing = false;
        //     Dispatcher.UIThread.Post(() => { UIManager.ClosingWindowSet.Remove(this); },
        //         DispatcherPriority.ApplicationIdle);
        // }, DispatcherPriority.ApplicationIdle);
    }

    //Tools
    protected void ShowMessage(string message)
    {
        App.MessageService.ShowMessageBox(message, this.GetParentWindow());
    }
}