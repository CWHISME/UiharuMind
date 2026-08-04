using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Input;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Views.Common;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Views.Windows;

public partial class PermissionGuideWindow : UiharuWindowBase
{
    private bool _firstActivation = true;
    private bool _hookFailed;
    private bool _skipCloseCheck;

    public PermissionGuideWindow()
    {
        InitializeComponent();

        AccessibilitySettingsButton.Click += OnOpenAccessibilitySettings;
        ScreenRecordingSettingsButton.Click += OnOpenScreenRecordingSettings;
        SkipButton.Click += OnActionButton;

        Activated += OnWindowActivated;
        RefreshPermissions();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_firstActivation)
        {
            _firstActivation = false;
            return;
        }

        RefreshPermissions();
        TryHook();
    }

    private void RefreshPermissions()
    {
        if (PlatformUtils.IsMacOS)
        {
            UpdatePermissionStatus(
                MacPermissionService.IsAccessibilityGranted(),
                AccessibilityStatusDot,
                AccessibilityStatusText,
                AccessibilitySettingsButton);

            UpdatePermissionStatus(
                MacPermissionService.IsScreenRecordingGranted(),
                ScreenRecordingStatusDot,
                ScreenRecordingStatusText,
                ScreenRecordingSettingsButton);
        }
        else
        {
            UpdatePermissionStatus(true, AccessibilityStatusDot, AccessibilityStatusText, AccessibilitySettingsButton);
            UpdatePermissionStatus(true, ScreenRecordingStatusDot, ScreenRecordingStatusText, ScreenRecordingSettingsButton);
        }
    }

    private static void UpdatePermissionStatus(
        bool granted,
        Ellipse statusDot,
        TextBlock statusText,
        Button settingsButton)
    {
        if (granted)
        {
            statusDot.Classes.Clear();
            statusDot.Classes.Add("StatusDotGranted");
            statusText.Classes.Clear();
            statusText.Classes.Add("StatusGranted");
            statusText.Text = LocalizationManager.Instance.GetString("PermissionGranted");
            settingsButton.IsVisible = false;
        }
        else
        {
            statusDot.Classes.Clear();
            statusDot.Classes.Add("StatusDotDenied");
            statusText.Classes.Clear();
            statusText.Classes.Add("StatusDenied");
            statusText.Text = LocalizationManager.Instance.GetString("PermissionNotGranted");
            settingsButton.IsVisible = true;
        }
    }

    private void OnOpenAccessibilitySettings(object? sender, RoutedEventArgs e)
    {
        if (PlatformUtils.IsMacOS)
            MacPermissionService.OpenAccessibilitySettings();
    }

    private void OnOpenScreenRecordingSettings(object? sender, RoutedEventArgs e)
    {
        if (PlatformUtils.IsMacOS)
            MacPermissionService.OpenScreenRecordingSettings();
    }

    private void TryHook()
    {
        if (!MacPermissionService.IsAccessibilityGranted()) return;
        InputManager.Instance.Start(OnQuickKeyInitFailure);
    }

    private void OnQuickKeyInitFailure()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _hookFailed = true;
            var content = (SkipButton.Content as StackPanel)?.Children
                .OfType<TextBlock>().FirstOrDefault();
            if (content != null)
                content.Text = LocalizationManager.Instance.GetString("PermissionRestart");
        });
    }

    private async void OnActionButton(object? sender, RoutedEventArgs e)
    {
        if (_hookFailed)
        {
            await ShowRestartConfirm();
        }
        else
        {
            await ShowSkipConfirm();
        }
    }

    private async Task ShowSkipConfirm()
    {
        var messageService = App.Services.GetRequiredService<IMessageService>();
        string confirmMessage = LocalizationManager.Instance.GetString("PermissionSkipConfirm");
        string confirmTitle = LocalizationManager.Instance.GetString("PermissionSkipConfirmTitle");

        bool confirmed = await messageService.ConfirmAsync(confirmMessage, confirmTitle);
        if (confirmed)
        {
            _skipCloseCheck = true;
            Close();
        }
    }

    private static async Task ShowRestartConfirm()
    {
        var messageService = App.Services.GetRequiredService<IMessageService>();
        string confirmMessage = LocalizationManager.Instance.GetString("PermissionRestartConfirm");
        string confirmTitle = LocalizationManager.Instance.GetString("PermissionRestart");

        bool confirmed = await messageService.ConfirmAsync(confirmMessage, confirmTitle);
        if (confirmed)
        {
            ApplicationRestartService.Restart();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_skipCloseCheck)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (_hookFailed)
        {
            _ = ShowRestartConfirm();
        }
        else
        {
            _ = ShowSkipConfirm();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_hookFailed && MacPermissionService.IsAccessibilityGranted())
        {
            InputManager.Instance.Start(() => { });
        }
    }
}
