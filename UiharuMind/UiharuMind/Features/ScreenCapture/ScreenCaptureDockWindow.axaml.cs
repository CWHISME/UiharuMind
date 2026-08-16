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
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.QuickChat;

namespace UiharuMind.Features.ScreenCapture;

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
        // OcrBtn.IsVisible = PlatformUtils.IsMacOS;
    }

    private void OnOcrBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        var path = Path.GetTempPath() + "ocr.png";
        CurrentSnapWindow!.ImageSource!.Save(path);
        ScreenCaptureManager.OpenOcr(path, (int)CurrentSnapWindow.Width, (int)CurrentSnapWindow.Height);
    }

    private void OnCopyBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        App.Clipboard.CopyImageToClipboard(CurrentSnapWindow!.ImageSource!, true);
    }

    private async void OnSaveBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        await App.FilesService.SaveImageAsync(CurrentSnapWindow!.ImageSource!, CurrentSnapWindow);
    }

    private void OnOcrAiBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        // ImageOcrPromptAction skill = new ImageOcrPromptAction(GetImageBytes());
        CustomImageSkill skill = new CustomImageSkill(DefaultCharacter.VisionOcr, GetImageBytes());
        QuickChatResultWindow.Show("OCR (AI)", "", skill);
    }

    private void OnExplainAiBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        CustomImageSkill skill = new CustomImageSkill(DefaultCharacter.AssistantExplain, GetImageBytes());
        QuickChatResultWindow.Show(Lang.Explain, "", skill);
    }

    private void OnVisionAiBtnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null) return;
        // 快速提问窗是缓存窗、活得比预览窗久,而这张图归预览窗所有(关窗即释放)。
        // 直接把它递过去就是留下一个悬空引用:预览窗一关,那边发送时读到已释放的位图
        QuickStartChatWindow.Show(CurrentSnapWindow.ImageSource?.CloneBitmap());
    }

    private void OnEditBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        Bitmap? backup = CurrentSnapWindow!.ImageOriginSource;
        Bitmap? curImage = CurrentSnapWindow.ImageSource!;
        CurrentSnapWindow.ImageSource = null;
        CurrentSnapWindow!.ImageOriginSource = null;
        CurrentSnapWindow!.ImageBackupSource = null;
        var backupPos = CurrentSnapWindow.Position;
        ScreenCaptureEditWindow window = new ScreenCaptureEditWindow(
            curImage, backupPos,
            new Size(CurrentSnapWindow.Width, CurrentSnapWindow.Height), (bitmap) =>
            {
                CurrentSnapWindow.SetImage(bitmap, pos: backupPos);
                CurrentSnapWindow.ImageOriginSource = CurrentSnapWindow.ImageBackupSource = backup;

                App.Clipboard.CopyImageToClipboard(bitmap, true);
                App.Clipboard.RecordImageToHistory(bitmap);
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

    private bool IsValid()
    {
        if (CurrentSnapWindow == null || CurrentSnapWindow.ImageSource == null) return false;
        return true;
    }

    private byte[] GetImageBytes()
    {
        return CurrentSnapWindow!.ImageSource!.BitmapToBytes();
    }
}