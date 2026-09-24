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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
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
        // 基类给的是 BorderOnly：窗口自己还带一层不透明底，圆角胶囊外圈会漏出白边。
        // 这条工具条只是一颗浮在截图上的胶囊，要的是纯透明窗
        this.SetSimpledecorationPureWindow();
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ToggleOldNewBtn.IsVisible = CurrentSnapWindow?.ImageBackupSource != null;
        // OcrBtn.IsVisible = PlatformUtils.IsMacOS;
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 必须压在钉图窗之上：贴图为投影留的那圈透明留白会盖到工具条头上，
        // 同档位时点一下贴图就把工具条压下去了
        OverlayWindowService.ApplyNativeWindowLevel(this, EOverlayWindowLevel.PinnedDock);
        // 平台隔离：有没有系统 OCR 只问工厂，业务代码不写平台分支
        OcrTextBtn.IsVisible = ScreenCapturePreviewWindow.OcrSupported;
        OcrTextBtn.IsChecked = CurrentSnapWindow?.OcrMode == true;
    }

    private void OnOcrTextToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (CurrentSnapWindow == null || !ScreenCapturePreviewWindow.OcrSupported) return;
        CurrentSnapWindow.SetOcrMode(OcrTextBtn.IsChecked == true);
    }

    private void OnCopyBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        // 预览窗自己会在换图/关窗时释放 ImageSource,不能把它交给剪贴板(剪贴板要留到有人粘贴),故复制一份移交
        Bitmap? forClipboard = CurrentSnapWindow!.ImageSource!.CloneBitmap();
        if (forClipboard != null) App.Clipboard.CopyImageToClipboard(forClipboard, true);
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
        CustomImageSkill skill = new CustomImageSkill(DefaultCharacter.ImageOcrPrompt,
            new ImageInput(GetImageBytes(), "image/png")); //BitmapToBytes 产出一向是 PNG
        QuickChatResultWindow.Show("OCR (AI)", "", skill);
    }

    private void OnExplainAiBtnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsValid()) return;
        CustomImageSkill skill = new CustomImageSkill(DefaultCharacter.ExplainPrompt,
            new ImageInput(GetImageBytes(), "image/png")); //BitmapToBytes 产出一向是 PNG
        QuickChatResultWindow.Show(Loc.Text(LangKey.Explain), "", skill);
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
            CurrentSnapWindow.DisplaySize, (bitmap) =>
            {
                CurrentSnapWindow.SetImage(bitmap, pos: backupPos);
                CurrentSnapWindow.ImageOriginSource = CurrentSnapWindow.ImageBackupSource = backup;

                // bitmap 已交给上面的 SetImage(预览窗接管并会释放),剪贴板那份必须是独立的一张
                Bitmap? forClipboard = bitmap.CloneBitmap();
                if (forClipboard != null) App.Clipboard.CopyImageToClipboard(forClipboard, true);
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