using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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
    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public ImageDrawWindow()
    {
        InitializeComponent();
        // 整窗接拖放：拖进来的图当参考图
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
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
            AppPaths.Data.Images);
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
}
