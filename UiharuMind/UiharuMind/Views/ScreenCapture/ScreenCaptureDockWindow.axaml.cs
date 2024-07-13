using Avalonia.Interactivity;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.ScreenCapture;

public partial class ScreenCaptureDockWindow : DockWindow
{

    public ScreenCaptureDockWindow()
    {
        InitializeComponent();
    }

    private void OnOcrBtnClock(object? sender, RoutedEventArgs e)
    {
        ScreenCaptureManager.OpenOcr("");
    }
}