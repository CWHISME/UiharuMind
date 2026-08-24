using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Services.Permissions;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Input;

namespace UiharuMind.Shared.Windows;

public partial class PermissionGuideWindow : UiharuWindowBase
{
    private readonly IPlatformPermissionProvider _permissionProvider = PlatformPermissionProviderFactory.Create();
    private bool _firstActivation = true;
    private bool _hookFailed;
    private bool _skipCloseCheck;

    public PermissionGuideWindow()
    {
        InitializeComponent();

        PermissionItemsControl.ItemsSource = _permissionProvider.Items;
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

    private async void RefreshPermissions()
    {
        await _permissionProvider.RefreshAsync();
    }

    private void TryHook()
    {
        if (!_permissionProvider.IsInputHookAllowed) return;
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
        if (!_hookFailed && _permissionProvider.IsInputHookAllowed)
        {
            InputManager.Instance.Start(() => { });
        }
    }
}
