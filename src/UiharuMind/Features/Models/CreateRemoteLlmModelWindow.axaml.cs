using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
            DataContext = new RemoteModelEditViewData(remoteModelInfo)
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
}
