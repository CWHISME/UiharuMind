using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models.ProviderPicker;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models;

public partial class CreateRemoteLlmModelWindow : Window
{
    private readonly IMessageService? _messages; //切换供应商前的确认；无 DI（预览/测试）时为 null
    private ProviderPickerViewData? _providerPicker; //与窗口同寿命：一次开窗建一个

    /// <summary>
    /// 打开创建/编辑远程模型对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="remoteModelInfo">要编辑的模型,为空表示创建</param>
    /// <returns>确认时返回承载最终配置的模型信息,取消返回空</returns>
    public static async Task<RemoteModelInfo?> ShowWindow(Window owner, RemoteModelInfo? remoteModelInfo = null)
    {
        RemoteModelEditViewData viewModel = new(remoteModelInfo,
            name => LlmManager.Instance.TryGetRemoteModelInfo(name, out _));
        var window = new CreateRemoteLlmModelWindow
        {
            DataContext = viewModel,
        };
        // 本地没有 models.dev 数据才联网拉一次，不挡表单（ADR 0103）
        _ = viewModel.EnsureProviderDataAsync();
        return await window.ShowDialog<RemoteModelInfo>(owner);
    }

    public CreateRemoteLlmModelWindow()
    {
        InitializeComponent();
        _messages = App.Services?.GetService(typeof(IMessageService)) as IMessageService;
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

    /// <summary>点身份条右侧按钮：弹层由 Flyout 自己开，这里只兜住「编辑模式不给换」</summary>
    private void OpenProviderPicker_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteModelEditViewData { CanSwitchProvider: false }) return;
        (sender as Button)?.Flyout?.Hide();
    }

    /// <summary>
    /// 弹层每次打开都重挂一次数据：选择器一次开窗建一个，重复打开只重建列表，不重复拉网络
    /// </summary>
    private void OnProviderPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not RemoteModelEditViewData viewModel) return;

        _providerPicker ??= new ProviderPickerViewData(provider => PickProviderAsync(viewModel, provider));
        ProviderPicker.DataContext = _providerPicker;
        _ = _providerPicker.BeginAsync();
    }

    /// <summary>
    /// 选中一家供应商。表单已经填过东西时先确认一次——切换会把整张表单重置掉
    /// </summary>
    private async Task PickProviderAsync(RemoteModelEditViewData viewModel, RemoteProviderInfo provider)
    {
        bool changed = viewModel.CatalogProvider?.Id != provider.Id;
        if (changed && viewModel.HasProviderSwitchContent && _messages is not null &&
            !await _messages.ConfirmAsync(Loc.Text(LangKey.ProviderSwitchConfirm)))
        {
            return;
        }

        viewModel.ApplyCatalogProvider(provider);
        ProviderPickerButton.Flyout?.Hide();
    }
}
