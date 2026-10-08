using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ChatModels;

/// <summary>
/// 「对话模型」页签
/// </summary>
public partial class ChatModelListView : UserControl
{
    public ChatModelListView()
    {
        InitializeComponent();
    }

    private void OnModelParamsClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ModelRunningData model && TopLevel.GetTopLevel(this) is Window owner)
            _ = ModelRuntimeOverridesWindow.ShowWindow(owner, model);
    }

    // 只有本地模型有菜单；按行建，菜单关了就丢，不在每一行模板里各挂一份
    private void OnModelListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ListBoxItem? row = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (row?.DataContext is not ModelRunningData { IsRemoteModel: false } model || DataContext is not ChatModelListViewData data ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        MenuItem parameters = new() { Header = Loc.Text(LangKey.ModelParamsMenu) };
        parameters.Click += (_, _) => _ = ModelRuntimeOverridesWindow.ShowWindow(owner, model);
        List<MenuItem> items = [parameters];
        if (ChatModelListViewData.CanDeleteLocalModel(model))
        {
            items.Add(new MenuItem
            {
                Header = Loc.Text(LangKey.ModelDeleteFiles),
                Command = data.DeleteLocalModelCommand,
                CommandParameter = model
            });
        }

        ContextMenu menu = new()
        {
            ItemsSource = items,
            Placement = e.TryGetPosition(null, out _) ? PlacementMode.Pointer : PlacementMode.Bottom
        };
        menu.Open(row);
        e.Handled = true;
    }
}
