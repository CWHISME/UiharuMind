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

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models
{
    public partial class ModelPage : UserControl
    {
        public ModelPage()
        {
            InitializeComponent();
        }

        // 只有本地模型有菜单；按行建，菜单关了就丢，不在每一行模板里各挂一份
        private void OnModelListContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            ListBoxItem? row = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (row?.DataContext is not ModelRunningData { IsRemoteModel: false } model || DataContext is not ModelPageData data ||
                TopLevel.GetTopLevel(this) is not Window owner)
                return;

            MenuItem parameters = new() { Header = Loc.Text(LangKey.ModelParamsMenu) };
            parameters.Click += (_, _) => _ = ModelRuntimeOverridesWindow.ShowWindow(owner, model);
            List<MenuItem> items = [parameters];
            if (ModelPageData.CanDeleteLocalModel(model))
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
}