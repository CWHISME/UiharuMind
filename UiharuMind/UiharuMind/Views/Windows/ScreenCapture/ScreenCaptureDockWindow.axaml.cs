using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.ScreenCapture;

public partial class ScreenCaptureDockWindow : DockWindow<ScreenCapturePreviewWindow>
{
    public ScreenCaptureDockWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        InitializeComponent();
    }

    private void OnOcrBtnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null) return;
        var path = Path.GetTempPath() + "ocr.png";
        CurrentSnapWindow.ImageSource.Save(path);
        ScreenCaptureManager.OpenOcr(path, (int)CurrentSnapWindow.Width, (int)CurrentSnapWindow.Height);
    }

    private void OnCopyBtnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null) return;
        App.Clipboard.CopyImageToClipboard(CurrentSnapWindow.ImageSource);
    }

    private async void OnSaveBtnClick(object? sender, RoutedEventArgs e)
    {
        await App.Clipboard.GetImageFromClipboard();
    }
}