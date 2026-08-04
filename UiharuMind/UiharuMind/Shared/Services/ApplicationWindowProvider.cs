using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Shared.Services;

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
        return _screensService.GetActiveWindow();
    }

    public Screen GetTargetScreen()
    {
        return _screensService.GetSafeActivationScreen();
    }
}