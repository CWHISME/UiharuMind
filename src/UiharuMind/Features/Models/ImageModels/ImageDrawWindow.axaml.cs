using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core;
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
                path => App.FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path)),
            AppPaths.Data.Images);
        window.DataContext = data;
        window.Closed += (_, _) => data.Dispose();
        // 主窗口收在托盘时不挂父窗口，否则 Show 直接抛
        if (owner.IsVisible) window.Show(owner);
        else window.Show();
    }
}
