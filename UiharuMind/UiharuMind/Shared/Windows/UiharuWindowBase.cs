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
using UiharuMind.Shared.Windows.Focus;

namespace UiharuMind.Shared.Windows;

public abstract class UiharuWindowBase : Window
{
    private IMessageService MessageService =>
        App.Services.GetRequiredService<IMessageService>();
    public event Action? OnPreCloseEvent;

    protected int StartWidth;
    protected int StartHeight;

    private readonly IWindowFocusBehavior _focusBehavior = WindowFocusBehaviorFactory.Create();

    /// <summary>
    /// 是否不关闭，重复复用
    /// </summary>
    public virtual bool IsCacheWindow => false;

    public virtual bool ContributesToMacRegularMode => true;

    /// <summary>
    /// 是否是辅助窗口（快捷面板、浮窗、钉图这类跟随操作弹出的窗口）。
    /// 辅助窗口取焦点时不该惊动应用的其它窗口，具体怎么做由
    /// <see cref="Focus.IWindowFocusBehavior"/> 按平台决定。
    /// </summary>
    public virtual bool IsAuxiliaryWindow => !ContributesToMacRegularMode;

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
        _focusBehavior.PrepareShow(this);
        OnPreShow();
        if (isFirstShow)
        {
            if (IconUtils.DefaultAppIcon != null) Icon = new WindowIcon(IconUtils.DefaultAppIcon);
            StartWidth = (int)Width;
            StartHeight = (int)Height;
            Show();
            OnPostShow();
            _focusBehavior.AfterShow(this);
            // 必须先切 Dock 图标/激活策略再取焦点：accessory→regular 的 TransformProcessType
            // 会把刚做的激活清掉，窗口就留在别的应用下面了（复用分支本来就是这个顺序）
            UIManager.RefreshMacApplicationActivationPolicy();
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
                _focusBehavior.AfterShow(this);
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
    /// 让窗口取得焦点。辅助窗口不会惊动应用的其它窗口。
    /// </summary>
    public void RequestFocus() => _focusBehavior.Focus(this);

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
        _focusBehavior.PrepareClose(this);
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
