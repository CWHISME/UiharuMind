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

using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

public abstract class UiharuWindowBase : Window
{
    public void RequestShow(bool isFirstShow = false, bool isActivate = true)
    {
        OnPreShow();
        if (isFirstShow)
        {
            if (IconUtils.DefaultAppIcon != null) Icon = new WindowIcon(IconUtils.DefaultAppIcon);
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

    protected virtual void OnPreShow()
    {
    }

    protected virtual void OnPreClose()
    {
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = true;
        SafeClose();
    }

    protected virtual void SafeClose()
    {
        OnPreClose();
        // InvalidateMeasure();
        // Log.Debug("Closing window: " + this.GetType().Name + "   " + this.IsMeasureValid);
        Dispatcher.UIThread.Post(Hide, DispatcherPriority.ApplicationIdle);
    }

    //Tools
    protected void ShowMessage(string message)
    {
        App.MessageService.ShowMessageBox(message, this.GetParentWindow());
    }
}