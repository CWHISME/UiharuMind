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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Markup.Xaml;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Features.Models;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Features.Conversation.Pages;

public partial class ModelSelectComboBoxView : UserControl
{
    public ModelSelectComboBoxView()
    {
        InitializeComponent();
        DataContext = App.ModelService;
    }
    
    /// <summary>
    /// 复制这一行的模型名。<b>复制 ModelName 而不是 ModelId</b>：前者是全局唯一 key
    /// （本地与远程同一命名空间，<c>LlmManager.CacheModelDictionary</c> 按它查），
    /// 后者是发给接口的标识，本地模型恒为空串。
    ///
    /// 走 Click 而不是 Command：ComboBox 的下拉项在弹出层里，
    /// 从 ItemTemplate 往上找宿主 DataContext 并不可靠。
    /// </summary>
    private void OnCopyModelNameClick(object? sender, RoutedEventArgs e)
    {
        // 吃掉事件,否则点复制会顺带把这一项选中——而选中本地模型意味着换模型
        e.Handled = true;
        if ((sender as Control)?.DataContext is not ModelRunningData model) return;
        if (string.IsNullOrEmpty(model.ModelName)) return;
        App.Clipboard.CopyToClipboard(model.ModelName, true);
        // 弹一条:剪贴板是不可见的,不给反馈用户只能再点一次确认
        App.Services.GetRequiredService<IMessageService>()
            .ShowNotification(Loc.Text("CopiedToClipboardTips"), severity: MessageSeverity.Success);
    }

    // private async void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    // {
    //     // Log.Debug("Select Model: " + (e.AddedItems.Count > 0 ? e.AddedItems[0] : 0));
    //     // await App.ModelService.LoadModel((e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as ModelRunningData);
    //     App.ModelService.CurModelRunningData = (e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as ModelRunningData;
    // }
}