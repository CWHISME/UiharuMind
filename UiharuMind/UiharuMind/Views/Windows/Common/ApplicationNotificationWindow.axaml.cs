using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Avalonia.Threading;
using UiharuMind.Services;
using UiharuMind.Utils;

namespace UiharuMind.Views.Windows.Common;

public partial class ApplicationNotificationWindow : Window
{
    private readonly IApplicationWindowProvider? _windowProvider;
    private readonly Dictionary<ApplicationNotification, Border> _notificationCards = [];
    private PixelPoint? _lastPosition;

    public ApplicationNotificationWindow()
    {
        InitializeComponent();
    }

    public ApplicationNotificationWindow(IApplicationWindowProvider windowProvider) : this()
    {
        _windowProvider = windowProvider;
        this.SetSimpledecorationPureWindow(true);
        ShowActivated = false;
        Focusable = false;
        ShowInTaskbar = false;
        CanResize = false;
    }

    public void ShowOrReposition()
    {
        Reposition();
        if (!IsVisible) Show();
        Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
    }

    public async Task PlayNotificationExitAsync(ApplicationNotification notification)
    {
        if (_notificationCards.TryGetValue(notification, out Border? card))
        {
            await UiAnimationUtils.PlayNotificationTransitionAnimationAsync(card, false);
        }
    }

    private void Reposition()
    {
        if (_windowProvider == null) return;

        Screen screen = _windowProvider.GetTargetScreen();
        double scaling = screen.Scaling;
        const int margin = 18;
        int width = (int)Math.Ceiling(Width * scaling);
        var nextPosition = new PixelPoint(
            screen.WorkingArea.Right - width - (int)(margin * scaling),
            screen.WorkingArea.Y + (int)(margin * scaling));
        if (_lastPosition == nextPosition) return;

        _lastPosition = nextPosition;
        Position = nextPosition;
    }

    private void Notification_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Border { DataContext: ApplicationNotification notification } border) return;

        _notificationCards[notification] = border;
        if (notification.HasPlayedEntranceAnimation) return;

        notification.HasPlayedEntranceAnimation = true;
        _ = UiAnimationUtils.PlayNotificationTransitionAnimationAsync(border, true);
    }

    private void Notification_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border { DataContext: ApplicationNotification notification })
        {
            _notificationCards.Remove(notification);
        }
    }

    private static void Notification_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border { DataContext: ApplicationNotification notification })
            notification.IsPaused = true;
    }

    private static void Notification_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border { DataContext: ApplicationNotification notification })
            notification.IsPaused = false;
    }
}
