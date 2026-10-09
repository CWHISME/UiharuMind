using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 画图窗口：以后的自定义画图从这里长。后台代码只管开窗、接上出图服务与系统调用
/// </summary>
public partial class ImageDrawWindow : Window
{
    // 粘贴参考图落盘到系统临时目录：只服务本窗的出图，混进画好的图库（AppPaths.Data.Images）会污染图库
    private static readonly string PasteDirectory = Path.Combine(Path.GetTempPath(), "UiharuMind", "DrawPaste");

    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public ImageDrawWindow()
    {
        InitializeComponent();
        // 整窗接拖放：拖进来的图当参考图
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // 整窗接粘贴：Tunnel 阶段先截获。只在提示词输入框里生效——剪贴板有图就加参考图；
        // 不置 Handled，剪贴板是文本时输入框照常粘贴；焦点在别处不响应
        AddHandler(KeyDownEvent, OnKeyDownPreview, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// 打开画图窗口（不模态，可以开着边看边改提示词）
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="selected">默认选中的生图模型；null 选第一个</param>
    public static void ShowWindow(Window owner, ImageModelInfo? selected)
    {
        ImageDrawWindow window = new();
        ImageDrawViewData data = new(ImageModelSettingConfig.Current.Models, selected,
            ImageGenerationService.Shared.GenerateWithAsync,
            new ImageDrawActions(
                path => App.Clipboard.CopyImageToClipboard(new Bitmap(path)),
                path => App.FilesService.SaveImageAsync(new Bitmap(path), window, Path.GetFileName(path)),
                FileOpener.OpenPreviewImage,
                path => App.FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path),
                () => App.FilesService.SelectFilesAsync(window, Loc.Text(LangKey.ImageDrawReferences),
                    "*.png", "*.jpg", "*.jpeg", "*.webp")),
            AppPaths.Data.Images, LocalMemoryHolders.Current);
        window.DataContext = data;
        window.Closed += (_, _) => data.Dispose();
        // 主窗口收在托盘时不挂父窗口，否则 Show 直接抛
        if (owner.IsVisible) window.Show(owner);
        else window.Show();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Any(f => f == DataFormat.File)) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ImageDrawViewData data || data.IsGenerating) return;
        List<string> paths = e.DataTransfer.Items
            .Select(x => (x.TryGetRaw(DataFormat.File) as IStorageItem)?.TryGetLocalPath())
            .OfType<string>().ToList();
        e.Handled = true;
        await data.References.AddAsync(paths);
    }

    private async void OnKeyDownPreview(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!command || e.Key != Key.V) return;
        if (DataContext is not ImageDrawViewData data || data.IsGenerating) return;
        // 只在提示词输入框里粘贴图片；TextBox 的聚焦元素可能是 TextBox 自身或模板里的 TextPresenter
        if (FocusManager?.GetFocusedElement() is not TextBox and not TextPresenter) return;

        // 不置 Handled：剪贴板是文本时输入框照常粘贴；这里是图片时加参考图
        using Bitmap? bitmap = await App.Clipboard.GetImageFromClipboard();
        if (bitmap == null || !IsVisible) return;
        await data.References.AddPastedBitmapAsync(bitmap, PasteDirectory);
    }
}
