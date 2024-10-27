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
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.ViewModels.Pages;
using UiharuMind.Views;
using UiharuMind.Views.Common;
using Ursa.Common;
using Ursa.Controls;
using Ursa.Controls.Options;
using Notification = Avalonia.Controls.Notifications.Notification;
using WindowNotificationManager = Avalonia.Controls.Notifications.WindowNotificationManager;

namespace UiharuMind.Services;

/// <summary>
/// UI 信息弹出展示公共接口
/// </summary>
public partial class MessageService : ObservableObject
{
    [ObservableProperty] private bool _isBusy;

    // private readonly DummyWindow _target;
    private WindowNotificationManager? _notificationManager;
    private WindowToastManager? _toastManager;

    public WindowNotificationManager NotificationManager
    {
        get
        {
            if (_notificationManager == null)
                _notificationManager = new WindowNotificationManager(UIManager.GetWindow<MainWindow>())
                    { MaxItems = 6 };
            return _notificationManager;
        }
    }

    public WindowToastManager ToastManager
    {
        get
        {
            if (_toastManager == null)
                _toastManager = new WindowToastManager(UIManager.GetWindow<MainWindow>()) { MaxItems = 3 };
            return _toastManager;
        }
    }

    // public MessageService(DummyWindow target)
    // {
    //     _target = target;
    // }

    /// <summary>
    /// 显示弹窗提示
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="message"></param>
    public async void ShowMessageBox(string message, Window owner)
    {
        await ShowMessageBox(owner, message, Lang.MessageInfoTitle, MessageBoxIcon.Information, MessageBoxButton.OK);
    }

    /// <summary>
    /// 显示弹窗提示
    /// </summary>
    public async void ShowErrorMessageBox(string message, Window owner)
    {
        await ShowMessageBox(owner, message, Lang.MessageErrorTitle, MessageBoxIcon.Error, MessageBoxButton.OK);
    }

    /// <summary>
    /// 显示一个确认弹窗，可选择是、否
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public async Task<MessageBoxResult> ShowConfirmMessageBox(string message, Window owner)
    {
        return await ShowMessageBox(owner, message, Lang.MessageInfoTitle, MessageBoxIcon.Question,
            MessageBoxButton.YesNo);
    }

    /// <summary>
    /// 显示弹窗提示
    /// </summary>
    public async Task<MessageBoxResult> ShowMessageBox(Window owner, string message, string title, MessageBoxIcon icon,
        MessageBoxButton button)
    {
        // Window? mainWindow = UIManager.GetWindow<MainWindow>();
        // if (mainWindow?.IsVisible == false) mainWindow = _target;
        // owner ??= UIManager.GetRootWindow();
        if (IsBusy) return MessageBoxResult.None;
        IsBusy = true;
        MessageBoxResult result = MessageBoxResult.None;
        try
        {
            var messageWindow = new MessageBoxWindow(button)
            {
                Content = message,
                Title = title,
                MessageIcon = icon
            };
            messageWindow.Topmost = true;
            result = await messageWindow.ShowDialog<MessageBoxResult>(owner);
            //模态弹窗会闪，感觉是窗体渲染的问题，所以这里用非模态弹窗代替了
            // await MessageBox.ShowOverlayAsync(message, "Error", icon: MessageBoxIcon.Error,button: MessageBoxButton.OK);
        }
        catch (Exception e)
        {
            // ignored
        }
        finally
        {
            IsBusy = false;
        }

        return result;
    }

    //===========================非模态弹窗===========================

    /// <summary>
    /// 显示弹窗提示
    /// </summary>
    /// <param name="message"></param>
    /// <param name="callback"></param>
    public void ShowMessageBox(string message, Action<MessageBoxResult> callback)
    {
        ShowMessageBox(message, Lang.MessageInfoTitle, MessageBoxIcon.Information, MessageBoxButton.OK, callback);
    }

    /// <summary>
    /// 显示弹窗提示
    /// </summary>
    public void ShowErrorMessageBox(string message)
    {
        ShowMessageBox(message, Lang.MessageErrorTitle, MessageBoxIcon.Error, MessageBoxButton.OK, null);
    }

    /// <summary>
    /// 显示一个确认弹窗，可选择是、否
    /// </summary>
    /// <param name="message"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    public void ShowConfirmMessageBox(string message, Action<MessageBoxResult> callback)
    {
        ShowMessageBox(message, Lang.MessageInfoTitle, MessageBoxIcon.Question,
            MessageBoxButton.YesNo, callback);
    }

    public void ShowMessageBox(string message, string title, MessageBoxIcon icon, MessageBoxButton button,
        Action<MessageBoxResult>? callback)
    {
        if (IsBusy)
        {
            Log.Warning("MessageService is busy, can't show messagebox.");
            return;
        }

        IsBusy = true;
        var messageWindow = new UiharuMessageBoxWindow(button, (x) =>
        {
            IsBusy = false;
            callback?.Invoke(x);
        })
        {
            Content = message,
            Title = title,
            MessageIcon = icon
        };

        var mainWindow = UIManager.GetMainWindow();
        if (mainWindow == null) messageWindow.Show();
        else messageWindow.ShowDialog(mainWindow);
        messageWindow.Topmost = true;
    }

    //==================================================================================================

    /// <summary>
    /// 显示一个滑出的页面
    /// </summary>
    /// <param name="page"></param>
    /// <param name="position"></param>
    /// <param name="buttons"></param>
    /// <param name="canLightDismiss"></param>
    /// <param name="isCloseButtonVisible"></param>
    public async Task ShowPageDrawer(PageDataBase page, Position position = Position.Right,
        DialogButton buttons = DialogButton.None, bool canLightDismiss = true, bool isCloseButtonVisible = true)
    {
        var options = new DrawerOptions()
        {
            Position = position,
            Buttons = buttons,
            CanLightDismiss = canLightDismiss,
            IsCloseButtonVisible = isCloseButtonVisible,
            MinWidth = UIManager.GetWindow<MainWindow>()!.DesiredSize.Width * 0.66,
        };
        IsBusy = true;
        try
        {
            if (page.View.Parent is ContentControl parent) parent.Content = null;
            await Drawer.ShowCustomModal<object?>(page.View, page, null, options);
        }
        catch (Exception e)
        {
            // ignored
        }
        finally
        {
            IsBusy = false;
        }
    }

    //==================================================================================================

    /// <summary>
    /// 显示从界面右上方弹出的提示条信息
    /// </summary>
    public void ShowNotification(string message)
    {
        ShowNotification(Lang.MessageInfoTitle, message, NotificationType.Information);
    }

    /// <summary>
    /// 显示从界面右上方弹出的提示条信息
    /// </summary>
    public void ShowNotification(string title, string message, NotificationType type)
    {
        NotificationManager.Show(new Notification(title, message, type));
    }

    //==================================================================================================

    /// <summary>
    /// 显示从界面正上方弹出的提示条信息
    /// </summary>
    public void ShowToast(string message, NotificationType type = NotificationType.Information)
    {
        ToastManager.Show(message, type: type);
    }
}