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
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.QuickChat;

namespace UiharuMind.Features.ScreenCapture;

public partial class ScreenCaptureDockWindow : DockWindow<ScreenCapturePreviewWindow>
{
    public override EOverlayWindowLevel OverlayWindowLevel => EOverlayWindowLevel.PinnedDock;

    public ScreenCaptureDockWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        // 基类给的是 BorderOnly：窗口自己还带一层不透明底，圆角胶囊外圈会漏出白边。
        // 这条工具条只是一颗浮在截图上的胶囊，要的是纯透明窗
        this.SetSimpledecorationPureWindow();
        InitializeComponent();

        // 编辑工具条只发事件，这里做薄桥转发给贴图窗（编辑器归贴图窗持有）
        EditToolbar.ToolChanged += tool => CurrentSnapWindow?.SetEditTool(tool);
        EditToolbar.ColorChanged += color => CurrentSnapWindow?.SetEditColor(color);
        EditToolbar.UndoRequested += () => CurrentSnapWindow?.EditUndo();
        EditToolbar.RedoRequested += () => CurrentSnapWindow?.EditRedo();
        EditToolbar.SaveRequested += () => CurrentSnapWindow?.SaveEditMode();
        EditToolbar.CancelRequested += () => CurrentSnapWindow?.CancelEditMode();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RefreshBrowsePanel();
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 必须压在钉图窗之上：贴图为投影留的那圈透明留白会盖到工具条头上，
        // 同档位时点一下贴图就把工具条压下去了
        OverlayWindowService.ApplyNativeWindowLevel(this, EOverlayWindowLevel.PinnedDock);
        RefreshBrowsePanel();
    }

    // 两套按钮集宽度差一倍（浏览≈ 7 键、编辑≈ 6 工具 + 取色 + 撤销重做 + 保存取消）。
    // 窗口跟内容（SizeToContent），换面板会让窗口跟着长/缩——这是对的，但不能像早先那样
    // 「先给 MinWidth 撑大窗口、等 SizeChanged 再切面板」：SizeToContent 窗口不理 MinWidth，
    // 而改 MinWidth 也不会把布局标脏，于是尺寸不变、SizeChanged 永不到来——面板就永远停在浏览态。
    // 直接换面板 + 立刻跑一次布局：窗口会在同一次布局里尺寸到位，新面板排的就是新尺寸。

    protected override void OnMainWindowChanged(ScreenCapturePreviewWindow? previous, ScreenCapturePreviewWindow? current)
    {
        if (previous != null)
        {
            previous.EditModeChanged -= OnSnapEditModeChanged;
            previous.EditUndoRedoChanged -= OnSnapEditUndoRedoChanged;
        }

        if (current == null) return;

        current.EditModeChanged += OnSnapEditModeChanged;
        current.EditUndoRedoChanged += OnSnapEditUndoRedoChanged;
        OnSnapEditModeChanged(current.EditMode); // 初始对齐：复用时直接落在编辑态
    }

    private void OnSnapEditModeChanged(bool editMode)
    {
        Pinned = editMode; // 编辑工具条不能被「鼠标离开组合区域」藏掉，否则画着画着就没了
        ApplyPanelSwap(editMode);
        FitToCurrentPanel();
    }

    /// <summary>
    /// 把窗口定成当前面板的尺寸。
    /// <para>
    /// 不能靠「改 MinWidth 撑大 + 等 SizeChanged 再换面板」（旧做法）：窗口是 <c>SizeToContent</c>，
    /// 改 MinWidth 既不改变窗口尺寸也不标脏布局，SizeChanged 永不到来——面板永远停在浏览态。
    /// </para>
    /// <para>
    /// 也不能光靠 <c>SizeToContent</c> 自己跟：靠 <c>IsVisible</c> 换面板时，窗口的 DesiredSize 会变，
    /// 但排布不一定会重跑，实测窗口尺寸原地不动，编辑条就被排进浏览面板的宽度里（右边全裁掉）。
    /// 所以换完面板直接把窗口尺寸定成新面板量出来的值（就是 SizeToContent 本来该算出的那个）。
    /// </para>
    /// </summary>
    private void FitToCurrentPanel()
    {
        // 先排一遍：刚显示出来的面板模板还没应用、图标还没量出尺寸，直接量会少算一截（实测 403 vs 431）
        UpdateLayout();
        Size target = DesiredSize;
        if (target.Width <= 0 || target.Height <= 0) return;

        Width = target.Width;
        Height = target.Height;
        UpdateLayout();
    }

    private void ApplyPanelSwap(bool editMode)
    {
        BrowsePanel.IsVisible = !editMode;
        EditToolbar.IsVisible = editMode;

        if (!editMode)
        {
            RefreshBrowsePanel();
            return;
        }

        if (CurrentSnapWindow == null) return;
        EditToolbar.SetTool(CurrentSnapWindow.EditTool);
        EditToolbar.SetColor(CurrentSnapWindow.EditColor);
        EditToolbar.SetUndoRedo(CurrentSnapWindow.EditCanUndo, CurrentSnapWindow.EditCanRedo);
    }

    private void OnSnapEditUndoRedoChanged()
    {
        if (CurrentSnapWindow == null) return;
        EditToolbar.SetUndoRedo(CurrentSnapWindow.EditCanUndo, CurrentSnapWindow.EditCanRedo);
    }

    // 浏览面板的回显集中一处：开关状态跟随贴图窗（OCR 开关、改前/改后按钮、平台 OCR 能力）
    private void RefreshBrowsePanel()
    {
        ToggleOldNewBtn.IsVisible = CurrentSnapWindow?.ImageBackupSource != null;
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
        // 合并后编辑直接发生在贴图窗上：进入编辑模式，不再开第二个窗、不再 Hide/Show 贴图窗
        CurrentSnapWindow!.EnterEditMode();
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
