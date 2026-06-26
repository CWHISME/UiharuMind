using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using UiharuMind.Views;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.Services;

public interface IApplicationWindowProvider
{
    Window? GetActiveWindow();
    Screen GetTargetScreen();
}

public sealed class ApplicationWindowProvider : IApplicationWindowProvider
{
    private readonly ScreensService _screensService;

    public ApplicationWindowProvider(ScreensService screensService)
    {
        _screensService = screensService;
    }

    public Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.Windows
            .Where(window =>
                window is not DummyWindow and not ApplicationNotificationWindow &&
                window.IsVisible &&
                window.WindowState != WindowState.Minimized)
            .OrderByDescending(window => window.IsActive || window.IsFocused)
            .FirstOrDefault();
    }

    public Screen GetTargetScreen()
    {
        Window? activeWindow = GetActiveWindow();
        Screen? screen = activeWindow?.Screens.ScreenFromWindow(activeWindow);
        screen ??= _screensService.MouseScreen;
        screen ??= App.DummyWindow.Screens.Primary;
        return screen ?? App.DummyWindow.Screens.All[0];
    }
}
