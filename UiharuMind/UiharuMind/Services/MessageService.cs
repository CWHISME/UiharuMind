using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.Common;
using Ursa.Controls;

namespace UiharuMind.Services;

public sealed class MessageService : IMessageService, IDisposable
{
    private const int MaximumVisibleNotifications = 6;
    private static readonly TimeSpan DefaultNotificationDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorNotificationDuration = TimeSpan.FromSeconds(8);

    private readonly IApplicationWindowProvider _windowProvider;
    private readonly object _dialogSync = new();
    private readonly Queue<DialogRequest> _dialogQueue = [];
    private readonly Queue<NotificationRequest> _notificationQueue = [];
    private readonly ObservableCollection<ApplicationNotification> _notifications = [];

    private ApplicationNotificationWindow? _notificationWindow;
    private bool _dialogPumpRunning;
    private bool _disposed;

    public MessageService(IApplicationWindowProvider windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public async Task ShowInfoAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        await EnqueueDialogAsync(message, title ?? Lang.MessageInfoTitle,
            MessageBoxIcon.Information, MessageBoxButton.OK, cancellationToken);
    }

    public async Task ShowWarningAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        await EnqueueDialogAsync(message, title ?? Lang.MessageWarningTitle,
            MessageBoxIcon.Warning, MessageBoxButton.OK, cancellationToken);
    }

    public async Task ShowErrorAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        await EnqueueDialogAsync(message, title ?? Lang.MessageErrorTitle,
            MessageBoxIcon.Error, MessageBoxButton.OK, cancellationToken);
    }

    public Task<bool> ConfirmAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        return EnqueueDialogAsync(message, title ?? Lang.MessageInfoTitle,
            MessageBoxIcon.Question, MessageBoxButton.YesNo, cancellationToken);
    }

    public void ShowNotification(
        string message,
        string? title = null,
        MessageSeverity severity = MessageSeverity.Information)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message)) return;
        Dispatcher.UIThread.Post(() => EnqueueNotification(
            new NotificationRequest(title ?? GetDefaultTitle(severity), message, severity)));
    }

    private Task<bool> EnqueueDialogAsync(
        string message,
        string title,
        MessageBoxIcon icon,
        MessageBoxButton buttons,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);

        var request = new DialogRequest(message, title, icon, buttons, cancellationToken);
        lock (_dialogSync)
        {
            _dialogQueue.Enqueue(request);
            if (!_dialogPumpRunning)
            {
                _dialogPumpRunning = true;
                _ = ProcessDialogQueueAsync();
            }
        }

        return request.Completion.Task;
    }

    private async Task ProcessDialogQueueAsync()
    {
        while (true)
        {
            DialogRequest? request;
            lock (_dialogSync)
            {
                if (_dialogQueue.Count == 0)
                {
                    _dialogPumpRunning = false;
                    return;
                }

                request = _dialogQueue.Dequeue();
            }

            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
                continue;
            }

            try
            {
                bool result = await RunOnUiThreadAsync(() => ShowDialogCoreAsync(request));
                request.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                request.Completion.TrySetException(exception);
            }
        }
    }

    private async Task<bool> ShowDialogCoreAsync(DialogRequest request)
    {
        var completion = new TaskCompletionSource<MessageBoxResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new UiharuMessageBoxWindow(
            request.Buttons, result => completion.TrySetResult(result))
        {
            Content = request.Message,
            Title = request.Title,
            MessageIcon = request.Icon,
            Topmost = true
        };

        using CancellationTokenRegistration registration = request.CancellationToken.Register(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (window.IsVisible) window.Close();
            });
        });

        Window? owner = _windowProvider.GetActiveWindow();
        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Opened += (_, _) => CenterWindow(window);
            window.Show();
        }

        MessageBoxResult result = await completion.Task;
        request.CancellationToken.ThrowIfCancellationRequested();
        return result == MessageBoxResult.Yes || request.Buttons == MessageBoxButton.OK;
    }

    private void EnqueueNotification(NotificationRequest request)
    {
        EnsureNotificationWindow();
        ApplicationNotification? existingNotification = FindVisibleNotification(request);
        if (existingNotification != null)
        {
            existingNotification.Merge(GetNotificationDuration(request.Severity));
            return;
        }

        if (_notifications.Count >= MaximumVisibleNotifications)
        {
            _notificationQueue.Enqueue(request);
            return;
        }

        ShowNotificationCore(request);
    }

    private void ShowNotificationCore(NotificationRequest request)
    {
        var notification = new ApplicationNotification(
            request.Title,
            request.Message,
            request.Severity,
            CloseNotification);
        _notifications.Add(notification);
        notification.Start(GetNotificationDuration(request.Severity));
        _notificationWindow!.ShowOrReposition();
    }

    private ApplicationNotification? FindVisibleNotification(NotificationRequest request)
    {
        foreach (ApplicationNotification notification in _notifications)
        {
            if (notification.IsClosing) continue;
            if (notification.Severity == request.Severity &&
                string.Equals(notification.Title, request.Title, StringComparison.Ordinal) &&
                string.Equals(notification.Message, request.Message, StringComparison.Ordinal))
            {
                return notification;
            }
        }

        return null;
    }

    private void CloseNotification(ApplicationNotification notification)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => CloseNotification(notification));
            return;
        }

        if (notification.IsClosing) return;
        notification.IsClosing = true;
        _ = CloseNotificationAsync(notification);
    }

    private async Task CloseNotificationAsync(ApplicationNotification notification)
    {
        await (_notificationWindow?.PlayNotificationExitAsync(notification) ?? Task.CompletedTask);

        notification.Dispose();
        _notifications.Remove(notification);
        if (_notificationQueue.Count > 0)
        {
            ShowNotificationCore(_notificationQueue.Dequeue());
        }
        else if (_notifications.Count == 0)
        {
            _notificationWindow?.Hide();
        }
        else
        {
            _notificationWindow?.ShowOrReposition();
        }
    }

    private void EnsureNotificationWindow()
    {
        if (_notificationWindow != null) return;
        _notificationWindow = new ApplicationNotificationWindow(_windowProvider)
        {
            DataContext = _notifications
        };
    }

    private void CenterWindow(Window window)
    {
        Screen screen = _windowProvider.GetTargetScreen();
        double scaling = screen.Scaling;
        int width = (int)Math.Ceiling(window.Bounds.Width * scaling);
        int height = (int)Math.Ceiling(window.Bounds.Height * scaling);
        window.Position = new PixelPoint(
            screen.WorkingArea.X + (screen.WorkingArea.Width - width) / 2,
            screen.WorkingArea.Y + (screen.WorkingArea.Height - height) / 2);
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private static string GetDefaultTitle(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Success => GetText("MessageSuccessTitle"),
        MessageSeverity.Warning => Lang.MessageWarningTitle,
        MessageSeverity.Error => Lang.MessageErrorTitle,
        _ => Lang.MessageInfoTitle
    };

    private static TimeSpan GetNotificationDuration(MessageSeverity severity) =>
        severity == MessageSeverity.Error ? ErrorNotificationDuration : DefaultNotificationDuration;

    private static string GetText(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ApplicationNotification notification in _notifications)
            notification.Dispose();
        _notifications.Clear();
        _notificationQueue.Clear();
        _notificationWindow?.Close();
        _notificationWindow = null;

        lock (_dialogSync)
        {
            while (_dialogQueue.Count > 0)
            {
                DialogRequest request = _dialogQueue.Dequeue();
                request.Completion.TrySetCanceled();
            }
        }
    }

    private sealed record NotificationRequest(
        string Title,
        string Message,
        MessageSeverity Severity);

    private sealed class DialogRequest
    {
        public string Message { get; }
        public string Title { get; }
        public MessageBoxIcon Icon { get; }
        public MessageBoxButton Buttons { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DialogRequest(
            string message,
            string title,
            MessageBoxIcon icon,
            MessageBoxButton buttons,
            CancellationToken cancellationToken)
        {
            Message = message;
            Title = title;
            Icon = icon;
            Buttons = buttons;
            CancellationToken = cancellationToken;
        }
    }
}

public partial class ApplicationNotification : ObservableObject, IDisposable
{
    private readonly Action<ApplicationNotification> _close;
    private CancellationTokenSource? _lifetime;

    public string Title { get; }
    public string Message { get; }
    public MessageSeverity Severity { get; }
    public string IconText { get; }
    public string CloseText { get; }

    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isClosing;
    [ObservableProperty] private bool _hasPlayedEntranceAnimation;
    [ObservableProperty] private int _repeatCount = 1;

    public bool HasRepeat => RepeatCount > 1;
    public string RepeatCountText => RepeatCount > 1 ? $"x{RepeatCount}" : string.Empty;

    public ApplicationNotification(
        string title,
        string message,
        MessageSeverity severity,
        Action<ApplicationNotification> close)
    {
        Title = title;
        Message = message;
        Severity = severity;
        IconText = severity switch
        {
            MessageSeverity.Success => "✓",
            MessageSeverity.Warning => "!",
            MessageSeverity.Error => "×",
            _ => "i"
        };
        _close = close;
        CloseText = Lang.ResourceManager.GetString(
            "NotificationClose", LocalizationManager.Instance.CurrentCulture) ?? "Close";
    }

    public void Start(TimeSpan duration)
    {
        _lifetime = new CancellationTokenSource();
        _ = RunLifetimeAsync(duration, _lifetime.Token);
    }

    public void Merge(TimeSpan duration)
    {
        RepeatCount++;
        OnPropertyChanged(nameof(HasRepeat));
        OnPropertyChanged(nameof(RepeatCountText));
        RestartLifetime(duration);
    }

    [RelayCommand]
    private void Close() => _close(this);

    private async Task RunLifetimeAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        TimeSpan remaining = duration;
        TimeSpan interval = TimeSpan.FromMilliseconds(100);
        try
        {
            while (remaining > TimeSpan.Zero)
            {
                await Task.Delay(interval, cancellationToken);
                if (!IsPaused) remaining -= interval;
            }

            _close(this);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RestartLifetime(TimeSpan duration)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        _ = RunLifetimeAsync(duration, _lifetime.Token);
    }

    public void Dispose()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }
}
