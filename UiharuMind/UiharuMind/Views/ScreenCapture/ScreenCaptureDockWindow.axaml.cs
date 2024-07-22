using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Capture;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.ScreenCapture;

public partial class ScreenCaptureDockWindow : DockWindow<ScreenCapturePreviewWindow>
{
    public ScreenCaptureDockWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        InitializeComponent();
    }

    private void OnOcrBtnClock(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null) return;
        var path = Path.GetTempPath() + "ocr.png";
        CurrentSnapWindow.ImageSource.Save(path);
        ScreenCaptureManager.OpenOcr(path, (int)CurrentSnapWindow.Width, (int)CurrentSnapWindow.Height);
    }

    private void OnCopyBtnClock(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null) return;
        App.Clipboard.CopyImageToClipboard(CurrentSnapWindow.ImageSource);
    }
}