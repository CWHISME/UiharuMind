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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
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
    public virtual bool IsCacheWindow => false;

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
        if (isFirstShow) OnInitWindowPosition();
        OnPreShow();
        if (isFirstShow)
        {
            if (IconUtils.DefaultAppIcon != null) Icon = new WindowIcon(IconUtils.DefaultAppIcon);
            StartWidth = (int)Width;
            StartHeight = (int)Height;
            Show();
        }
        else
        {
            if (!IsCacheWindow)
            {
                Log.Warning($"[{GetType().Name}] This window is not allowed to be reused.");
                return;
            }

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
        OnPreCloseEvent?.Invoke();
        OnPreClose();
        if (IsCacheWindow)
        {
            e.Cancel = true;
            // App.DummyWindow.Activate();
            Dispatcher.UIThread.Post(Hide);
        }
        else UIManager.RemoveWindow(this);

        base.OnClosing(e);
    }

    public virtual void SafeClose()
    {
        Dispatcher.UIThread.Post(Close);
    }

    public virtual void SafeClose(float delayTime)
    {
        Task.Run(async () =>
        {
            await Task.Delay((int)(1000 * delayTime));
            SafeClose();
        });
    }


    //Tools
    protected void ShowMessage(string message)
    {
        App.MessageService.ShowMessageBox(message, this.GetParentWindow());
    }
}