using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.QuickTools;

public partial class FileSearchWindow : UiharuWindowBase
{
    public override bool IsCacheWindow => true;

    // 快捷键唤出的弹窗：取焦点时不激活本应用，免得把后台的主界面一起抬到前台
    public override bool IsAuxiliaryWindow => true;

    public SearchViewModel ViewModel => (SearchViewModel)DataContext!;

    public FileSearchWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<SearchViewModel>();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        ViewModel.Initialize();
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        this.SetScreenCenterPosition();
    }

    // private void OnDirectorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    // {
    //     if (sender is ComboBox comboBox && comboBox.SelectedItem is string path)
    //     {
    //         ViewModel.SetDirectoryCommand.Execute(path);
    //     }
    // }

    private void OnDirectoryLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            var text = comboBox.Text;
            if (!string.IsNullOrEmpty(text))
            {
                ViewModel.SetDirectoryCommand.Execute(text);
            }
        }
    }

    private void OnDirectoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ComboBox comboBox)
        {
            var text = comboBox.Text;
            if (!string.IsNullOrEmpty(text) && text != ViewModel.CurrentDirectory)
            {
                ViewModel.SetDirectoryCommand.Execute(text);
            }

            e.Handled = true;
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnContextMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is SearchItem item)
        {
            OpenTarget(item, menuItem.Tag?.ToString());
        }
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source)
        {
            var listBox = source.FindAncestorOfType<ListBox>();
            if (listBox?.SelectedItem is SearchItem item)
            {
                OpenTarget(item);
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Any(f => f == DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(DataFormat.File) is IStorageItem storageItem)
            {
                var path = storageItem.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    if (Directory.Exists(path))
                    {
                        ViewModel.SetDirectoryCommand.Execute(path);
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            ViewModel.SetDirectoryCommand.Execute(dir);
                        }
                    }

                    return;
                }
            }
        }

        e.Handled = true;
    }

    private async void OpenTarget(SearchItem item, string? tag = null)
    {
        var fullPath = Path.GetFullPath(Path.Combine(ViewModel.CurrentDirectory, item.Path));

        string action = tag ?? "OpenDefault";
        // 双击默认动作与右键「用编辑器打开」(OpenEditor) 同一条路：分流收在 FileOpener
        // （文本进编辑窗、图片走贴图窗、其余走系统），内容搜索命中时带着行号定位。
        // 只有超大或不存在才落到下面的 OpenFile 系统链。
        // 右键「用系统打开」(OpenFile) 是纯系统打开，不走这里。
        if (action is "OpenDefault" or "OpenEditor")
        {
            if (File.Exists(fullPath) && TextFileOpenPolicy.IsWithinEditLimit(fullPath))
            {
                await FileOpener.OpenAsync(fullPath, item.IsContentSearch ? item.LineNumber : null);
                return;
            }

            action = "OpenFile";
        }

        switch (action)
        {
            // 系统打开与揭示目录的 mechanics 收在 FileOpener，这里只剩动作路由
            case "OpenFile":
                if (File.Exists(fullPath))
                    await FileOpener.OpenWithSystemAsync(fullPath);
                else
                    OpenTarget(item, "OpenDir");

                break;
            case "OpenDir":
                FileOpener.RevealInFolder(fullPath);
                break;
        }
    }
}