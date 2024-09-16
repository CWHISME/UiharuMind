using System;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Resources.Lang;
using UiharuMind.Views;
using Ursa.Controls;
using Notification = Avalonia.Controls.Notifications.Notification;
using WindowNotificationManager = Avalonia.Controls.Notifications.WindowNotificationManager;

namespace UiharuMind.Services;

/// <summary>
/// UI 信息弹出展示公共接口
/// </summary>
public partial class MessageService : ObservableObject
{
    [ObservableProperty] private bool _isBusy;

    private readonly DummyWindow _target;
    private WindowNotificationManager? _notificationManager;
    private WindowToastManager? _toastManager;

    public WindowNotificationManager NotificationManager
    {
        get
        {
            if (_notificationManager == null)
                _notificationManager = new WindowNotificationManager(_target.MainWindow) { MaxItems = 6 };
            return _notificationManager;
        }
    }

    public WindowToastManager ToastManager
    {
        get
        {
            if (_toastManager == null) _toastManager = new WindowToastManager(_target.MainWindow) { MaxItems = 3 };
            return _toastManager;
        }
    }

    public MessageService(DummyWindow target)
    {
        _target = target;
    }

    public async void ShowMessage(string message)
    {
        await ShowMessage(message, Lang.MessageInfoTitle, MessageBoxIcon.Information, MessageBoxButton.OK);
    }

    public async void ShowErrorMessage(string message)
    {
        await ShowMessage(message, Lang.MessageErrorTitle, MessageBoxIcon.Error, MessageBoxButton.OK);
    }

    public async Task ShowMessage(string message, string title, MessageBoxIcon icon, MessageBoxButton button)
    {
        if (IsBusy || _target.MainWindow == null) return;
        IsBusy = true;
        try
        {
            await MessageBox.ShowAsync(_target.MainWindow, message, title, icon, button);
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
    }


    public void ShowNotification(string message)
    {
        ShowNotification(Lang.MessageInfoTitle, message, NotificationType.Information);
    }

    public void ShowNotification(string title, string message, NotificationType type)
    {
        NotificationManager.Show(new Notification(title, message, type));
    }

    public void ShowToast(string message, NotificationType type = NotificationType.Information)
    {
        ToastManager.Show(message, type: type);
    }
}