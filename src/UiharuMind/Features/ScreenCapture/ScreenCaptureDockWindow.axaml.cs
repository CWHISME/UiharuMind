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
    private bool _dockSizeFixed; //是否已量过编辑工具条尺寸
    private double _editToolbarWidth; //编辑工具条（含胶囊留白）尺寸，供「先长窗再换面板」用
    private double _editToolbarHeight;
    private bool _growPending; //浏览→编辑：等窗口长大后再切面板，避免编辑面板被排进旧尺寸

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
        MeasureEditToolbarSize();
    }

    // 两套按钮集宽度差一倍（浏览 ≈ 7 键、编辑 ≈ 6 工具 + 取色 + 撤销重做 + 保存取消）。
    // 窗口跟随当前面板 SizeToContent，切换模式必然原生 resize，而 resize 是异步回灌的——
    // 新面板若在 resize 落定前就排进旧尺寸，会「被压扁」一帧。
    // 处理：编辑→浏览直接换（浏览面板小，放进宽窗立刻是对的）；
    // 浏览→编辑先把窗口长到编辑尺寸，等 SizeChanged 落定再换面板。这里只量一次编辑尺寸。
    private void MeasureEditToolbarSize()
    {
        if (_dockSizeFixed) return;
        _dockSizeFixed = true;

        bool editWasVisible = EditToolbar.IsVisible;
        EditToolbar.IsVisible = true;
        UpdateLayout();
        _editToolbarWidth = PillBorder.DesiredSize.Width;
        _editToolbarHeight = PillBorder.DesiredSize.Height;
        EditToolbar.IsVisible = editWasVisible;
    }

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

        if (!editMode)
        {
            // 编辑→浏览：浏览面板比编辑小，放进当前窗口立刻就是对的——先换面板，再放开 Min 让窗口缩回
            SizeChanged -= OnDockSizeChangedWhileGrowing;
            _growPending = false;
            MinWidth = 0;
            MinHeight = 0;
            ApplyPanelSwap(false);
            return;
        }

        // 浏览→编辑：先把窗口长到编辑尺寸；窗口已经够大就直接换，否则等 resize 落定（SizeChanged）再换
        if (!_dockSizeFixed || _editToolbarWidth <= 0) MeasureEditToolbarSize();
        if (_editToolbarWidth <= 0 || _editToolbarHeight <= 0)
        {
            ApplyPanelSwap(true); // 量不到尺寸（极端时序），退化为直接切换
            return;
        }

        MinWidth = _editToolbarWidth;
        MinHeight = _editToolbarHeight;

        if (ClientSize.Width >= _editToolbarWidth - 1 && ClientSize.Height >= _editToolbarHeight - 1)
        {
            ApplyPanelSwap(true);
            return;
        }

        _growPending = true;
        SizeChanged += OnDockSizeChangedWhileGrowing;
    }

    // 窗口长到编辑尺寸后触发：此时才把面板换成编辑工具条，杜绝「编辑面板排进旧尺寸」的压扁帧
    private void OnDockSizeChangedWhileGrowing(object? sender, SizeChangedEventArgs e)
    {
        SizeChanged -= OnDockSizeChangedWhileGrowing;
        if (!_growPending) return;
        _growPending = false;
        if (CurrentSnapWindow != null) ApplyPanelSwap(CurrentSnapWindow.EditMode);
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
