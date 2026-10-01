using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 生图模型的新建、编辑对话框；后台代码只管开窗与调系统，表单逻辑在 <see cref="ImageModelEditViewData"/>
/// </summary>
public partial class ImageModelEditWindow : Window
{
    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public ImageModelEditWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开新建/编辑生图模型的对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="source">要编辑的模型；null 为新建</param>
    /// <param name="takenNames">其他生图模型已用的名字</param>
    /// <returns>确认时为新实例，取消为 null</returns>
    public static Task<ImageModelInfo?> ShowWindow(Window owner, ImageModelInfo? source,
        IReadOnlyCollection<string> takenNames)
    {
        // 沿用密钥的候选取远程对话模型那张表:那边是唯一存密钥的地方
        ImageModelEditWindow window = new()
        {
            DataContext = new ImageModelEditViewData(source, takenNames,
                RemoteModelSettingConfig.Current.ModelInfos.Values.ToList()),
        };
        return window.ShowDialog<ImageModelInfo?>(owner);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageModelEditViewData { CanConfirm: true } viewData) return;
        Close(viewData.BuildResult());
    }

    private void OpenWebsite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageModelEditViewData { HasWebsite: true } viewData) return;
        _ = Launcher.LaunchUriAsync(new Uri(viewData.WebsiteUrl));
    }
}
