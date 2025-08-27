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
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UiharuMind.Core.AI.Character.Skills;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Utils;
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ToggleOldNewBtn.IsVisible = CurrentSnapWindow?.ImageBackupSource != null;
        OcrBtn.IsVisible = PlatformUtils.IsMacOS;
    }

    private void OnOcrBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        var path = Path.GetTempPath() + "ocr.png";
        CurrentSnapWindow.ImageSource.Save(path);
        ScreenCaptureManager.OpenOcr(path, (int)CurrentSnapWindow.Width, (int)CurrentSnapWindow.Height);
    }

    private void OnCopyBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        App.Clipboard.CopyImageToClipboard(CurrentSnapWindow.ImageSource, true);
    }

    private async void OnSaveBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        await App.FilesService.SaveImageAsync(CurrentSnapWindow.ImageSource, CurrentSnapWindow);
    }

    private void OnOcrAiBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        ImageOcrSkill skill = new ImageOcrSkill(CurrentSnapWindow.ImageSource.BitmapToBytes());
        QuickChatResultWindow.Show("OCR (AI)", "", skill);
    }

    private void OnVisionAiBtnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null) return;
        QuickStartChatWindow.Show(CurrentSnapWindow.ImageSource);
    }

    private void OnEditBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        ScreenCaptureEditWindow window = new ScreenCaptureEditWindow(
            CurrentSnapWindow.ImageSource, CurrentSnapWindow.Position,
            new Size(CurrentSnapWindow.Width, CurrentSnapWindow.Height), (bitmap) =>
            {
                CurrentSnapWindow.ImageBackupSource = CurrentSnapWindow.ImageOriginSource;
                CurrentSnapWindow.ImageSource = bitmap;
                CurrentSnapWindow.ImageContent.Source = bitmap;
                App.Clipboard.CopyImageToClipboard(bitmap, true);
                CurrentSnapWindow.Show();
            });
        SafeClose();
        CurrentSnapWindow.Hide();
        window.Show();
        // var result = await window.ShowDialog<Bitmap?>(CurrentSnapWindow);
        // if (result != null)
        // {
        //     CurrentSnapWindow?.SetImage(result);
        //     CurrentSnapWindow?.Show();
        // }
    }

    private void OnToggleOldNewBtnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null || CurrentSnapWindow?.ImageBackupSource == null) return;
        (CurrentSnapWindow.ImageSource, CurrentSnapWindow.ImageBackupSource) =
            (CurrentSnapWindow.ImageBackupSource, CurrentSnapWindow.ImageSource);
        CurrentSnapWindow.ImageContent.Source = CurrentSnapWindow.ImageSource;
    }

    private void OnExplainAiBtnClick(object? sender, RoutedEventArgs e)
    {
    }

    private bool IsValid()
    {
        if (CurrentSnapWindow == null || CurrentSnapWindow.ImageSource == null) return false;
        return true;
    }
}