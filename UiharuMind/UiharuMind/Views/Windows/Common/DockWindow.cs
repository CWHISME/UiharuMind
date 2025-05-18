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
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

public interface IDockedWindow
{
    public event Action? OnPreCloseEvent;
}

public class DockWindow<T> : UiharuWindowBase where T : Window, IDockedWindow
{
    protected T? CurrentSnapWindow;

    protected override bool IsAllowFocusOnOpen => false;

    public DockWindow()
    {
        this.SetSimpledecorationWindow();
        ShowActivated = false;
    }

    // protected override void OnOpened(EventArgs e)
    // {
    //     base.OnOpened(e);
    //     UiAnimationUtils.PlayAlphaTransitionAnimation(this.VisualChildren[0], true);
    // }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        MainWindow_OnMouseLeave(this, e);
    }

    public void SetMainWindow(T? mainWindow)
    {
        // Log.Debug($"SetMainWindow {mainWindow}");
        if (mainWindow is not { IsVisible: true })
        {
            SafeClose();
            // UiAnimationUtils.PlayAlphaTransitionAnimation(this.VisualChildren[0], false, SafeClose);
            return;
        }

        // if (this.VisualChildren.Count > 0) UiAnimationUtils.StopAnimation(this.VisualChildren[0]);

        if (ReferenceEquals(mainWindow, CurrentSnapWindow))
        {
            // Log.Debug($"SetMainWindow {mainWindow} ReferenceEquals");
            RequestShow(isActivate: false);
            UpdateFollowerWindowPosition();
            return;
        }

        if (CurrentSnapWindow != null)
        {
            CurrentSnapWindow.PositionChanged -= MainWindow_PositionChanged;
            CurrentSnapWindow.SizeChanged -= MainWindow_SizeChanged;
            CurrentSnapWindow.PointerExited -= MainWindow_OnMouseLeave;
            CurrentSnapWindow.Closing -= MainWindow_OnClose;
            CurrentSnapWindow.OnPreCloseEvent -= MainWindow_OnClose;
        }

        // Log.Debug($"SetMainWindow {mainWindow} set");

        CurrentSnapWindow = mainWindow;

        CurrentSnapWindow.PositionChanged += MainWindow_PositionChanged;
        CurrentSnapWindow.SizeChanged += MainWindow_SizeChanged;
        CurrentSnapWindow.PointerExited += MainWindow_OnMouseLeave;
        CurrentSnapWindow.Closing += MainWindow_OnClose;
        CurrentSnapWindow.OnPreCloseEvent += MainWindow_OnClose;

        RequestShow(isActivate: false);
        UpdateFollowerWindowPosition();
        // Log.Debug($"SetMainWindow {mainWindow} UpdateFollowerWindowPosition");
    }

    private void MainWindow_OnClose()
    {
        SetMainWindow(null);
    }

    private void MainWindow_OnClose(object? sender, WindowClosingEventArgs e)
    {
        SetMainWindow(null);
    }

    private void MainWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_OnMouseLeave(object? sender, PointerEventArgs e)
    {
        // Log.Debug($"MainWindow_OnMouseLeave {sender} {e}");
        if (!CheckInValidBounds()) SetMainWindow(null);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateFollowerWindowPosition();
    }

    /// <summary>
    /// 是否处于合适区域
    /// </summary>
    /// <returns></returns>
    private bool CheckInValidBounds()
    {
        if (CurrentSnapWindow == null) return false;

        var mousePos = App.ScreensService.MousePosition;
        var scaling = App.ScreensService.Scaling;
        //检测是否处于组合区域内
        double offset = 10f * scaling;
        var selfWindowBounds = new Rect(this.Position.X + offset, this.Position.Y + offset,
            this.Width * scaling - offset, this.Height * scaling - offset);
        var targetWindowBounds = new Rect(CurrentSnapWindow.Position.X + offset, CurrentSnapWindow.Position.Y + offset,
            CurrentSnapWindow.Width * scaling - offset, CurrentSnapWindow.Height * scaling - offset);
        // 计算组合区域，包括两个窗口之间的间距
        var combinedBounds = selfWindowBounds.Union(targetWindowBounds);
        if (!combinedBounds.Contains(new Point(mousePos.X, mousePos.Y))) return false;

        // Log.Debug(
        //     $" mousePos:{mousePos} targetWindowBounds:{targetWindowBounds.Right}  selfWindowBounds：{selfWindowBounds.Right}");

        // 根据高度决定检测 mainWindowBounds 还是 bottomWindowBounds 的宽度
        //注：靠下才这样额外检测
        if (mousePos.Y < (targetWindowBounds.Bottom - offset) && mousePos.X < (targetWindowBounds.Right - offset))
        {
            // 位于目标(上方)窗口高度内，且处于其宽度内
            return true;
        }

        //位于底部窗口高度内，且处于其宽度内
        if (mousePos.Y < (selfWindowBounds.Bottom - offset) && mousePos.X < (selfWindowBounds.Right - offset))
        {
            return true;
        }

        return false;
    }

    private void UpdateFollowerWindowPosition()
    {
        // Log.Debug($"UpdateFollowerWindowPosition {CurrentSnapWindow} is now {CurrentSnapWindow?.GetType()}");
        if (!IsVisible || CurrentSnapWindow == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSnapWindow == null)
                return;

            OnFollowTarget(CurrentSnapWindow.Position, CurrentSnapWindow.ClientSize);
        });
    }

    protected virtual void OnFollowTarget(PixelPoint targetPosition, Size targetSize)
    {
        targetSize *= App.ScreensService.Scaling;
        Position = new PixelPoint(targetPosition.X, targetPosition.Y + (int)targetSize.Height + 5);
    }
}