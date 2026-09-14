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
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Shared.Windows;

public abstract class UiharuWindowBase : Window
{
    private IMessageService MessageService =>
        App.Services.GetRequiredService<IMessageService>();
    public event Action? OnPreCloseEvent;

    protected int StartWidth;
    protected int StartHeight;

    private bool _isNonactivatingPanel; //macOS：已转成 nonactivating panel，取焦点时不必激活本应用

    /// <summary>
    /// 是否不关闭，重复复用
    /// </summary>
    public virtual bool IsCacheWindow => false;

    public virtual bool ContributesToMacRegularMode => true;

    /// <summary>
    /// macOS：是否是辅助窗口。辅助窗口走 nonactivating panel 路线取焦点——不激活本应用，
    /// 于是后台的主界面不会被一起抬到前台。浮窗、快捷面板、钉图窗都属于这一类。
    /// </summary>
    public virtual bool IsMacAuxiliaryWindow => !ContributesToMacRegularMode;

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
        // Avalonia 的 Show 带 activateIgnoringOtherApps:，会把本应用所有窗口整组抬到前台。
        // 浮窗改走 panel 路线自己取焦点，这里必须先把它关掉
        if (IsMacAuxiliaryWindow && PlatformUtils.IsMacOS) ShowActivated = false;
        OnPreShow();
        if (isFirstShow)
        {
            if (IconUtils.DefaultAppIcon != null) Icon = new WindowIcon(IconUtils.DefaultAppIcon);
            StartWidth = (int)Width;
            StartHeight = (int)Height;
            Show();
            OnPostShow();
            if (IsMacAuxiliaryWindow) _isNonactivatingPanel = MacPanelWindowService.TryMakeNonactivatingPanel(this);
            if (isActivate && IsAllowFocusOnOpen) RequestFocus();
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
                OnPostShow();
                if (IsMacAuxiliaryWindow) _isNonactivatingPanel = MacPanelWindowService.TryMakeNonactivatingPanel(this);
                UIManager.RefreshMacApplicationActivationPolicy();
                if (isActivate && IsAllowFocusOnOpen) RequestFocus();
            }, DispatcherPriority.ApplicationIdle);
        }
        // 
        // else
        // {
        //     this.WindowState = WindowState.Normal;
        // Dispatcher.UIThread.Post(Show, DispatcherPriority.ApplicationIdle);
        // }
    }

    /// <summary>
    /// 让窗口取得焦点。辅助窗口只取键盘焦点，不激活本应用，用户原来那个应用继续留在前台。
    /// </summary>
    public void RequestFocus()
    {
        if (!_isNonactivatingPanel)
        {
            WindowActivationService.Activate(this);
            return;
        }

        MacPanelWindowService.FocusPanel(this);
        // 置顶浮窗在 floating 层，后台应用也压得住；普通层级的（翻译、文件搜索）压不住前台应用，
        // 必须激活本应用才看得见——但只带自己上来
        if (!Topmost) MacPanelWindowService.ActivateAppForWindowOnly(this);
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

    protected virtual void OnPostShow()
    {
    }

    protected virtual void OnPreClose()
    {
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        OnPreCloseEvent?.Invoke();
        OnPreClose();
        // 关窗前掐断 macOS 的 key 改派，否则后台的主界面会被 orderFront 抬到最前
        MacWindowFocusGuard.SuppressKeyHandoff(this);
        if (IsCacheWindow)
        {
            e.Cancel = true;
            // App.DummyWindow.Activate();
            Dispatcher.UIThread.Post(() =>
            {
                Hide();
                UIManager.RefreshMacApplicationActivationPolicy();
            });
        }
        else
        {
            UIManager.RemoveWindow(this);
            UIManager.RefreshMacApplicationActivationPolicy();
        }

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
        _ = MessageService.ShowInfoAsync(message);
    }
}
