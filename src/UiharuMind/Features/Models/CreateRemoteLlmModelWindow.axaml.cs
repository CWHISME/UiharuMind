using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Features.Models;

public partial class CreateRemoteLlmModelWindow : Window
{
    /// <summary>
    /// 打开创建/编辑远程模型对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="remoteModelInfo">要编辑的模型,为空表示创建</param>
    /// <returns>确认时返回承载最终配置的模型信息,取消返回空</returns>
    public static async Task<RemoteModelInfo?> ShowWindow(Window owner, RemoteModelInfo? remoteModelInfo = null)
    {
        var window = new CreateRemoteLlmModelWindow
        {
            DataContext = new RemoteModelEditViewData(remoteModelInfo,
                name => LlmManager.Instance.TryGetRemoteModelInfo(name, out _))
        };
        return await window.ShowDialog<RemoteModelInfo>(owner);
    }

    public CreateRemoteLlmModelWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteModelEditViewData { CanConfirm: true } viewModel) return;
        Close(viewModel.BuildResult());
    }

    private void OpenWebsite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteModelEditViewData { SelectedProvider.WebsiteUrl: { Length: > 0 } url }) return;
        _ = Launcher.LaunchUriAsync(new Uri(url));
    }

    /// <summary>
    /// 箭头开候选浮层。**只负责开**：浮层开着时再点箭头，是 Popup 的 light dismiss 在
    /// 按下阶段就把它关掉并按下了这次点击——这里若是 toggle，就会把它又打开一遍，变成关不掉。
    /// 万一某平台不吞这次点击，走到这里也只会 return，不会重新弹开。
    /// </summary>
    private void ToggleModelPicker_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteModelEditViewData viewModel) return;
        if (viewModel.IsModelPickerOpen) return;
        viewModel.IsModelPickerOpen = true;
    }

    /// <summary>点选候选：写回 ID 并收起浮层</summary>
    private void ModelOption_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteModelEditViewData viewModel) return;
        if ((sender as Control)?.DataContext is not ModelIdOptionItem option) return;
        viewModel.SelectModelIdOption(option);
    }
}
