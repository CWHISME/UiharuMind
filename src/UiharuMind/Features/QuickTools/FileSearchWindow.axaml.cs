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

    // 末段可编辑路径框（PathPicker 式）：粘长路径直达，键盘优先工具顺手。回车/失焦跳。
    private void OnPathBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            string text = (textBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(text) && text != ViewModel.CurrentDirectory)
            {
                ViewModel.SetDirectoryCommand.Execute(text);
            }
        }
    }

    private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            string text = (textBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(text) && text != ViewModel.CurrentDirectory)
            {
                ViewModel.SetDirectoryCommand.Execute(text);
            }

            e.Handled = true;
        }
    }

    // 点面包屑任一段回退到那个父目录（DataContext 是 DirSegment）
    private void OnBreadcrumbSegmentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is UiharuMind.Features.QuickTools.DirSegment segment)
        {
            ViewModel.NavigateToSegmentCommand.Execute(segment);
        }
    }

    // 目录条目单击下钻一层（文件条目单击只选中，打开仍走双击/右键）。
    // 条目取自点中处所在的 ListBoxItem.DataContext，不赌选中时序——
    // 靠 SelectedItem 的话，点到 A 时若上次选中的还是 B，会进错目录。
    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source
            && source.FindAncestorOfType<ListBoxItem>()?.DataContext is SearchItem item
            && item.IsDirectory)
        {
            ViewModel.EnterDirectoryCommand.Execute(item);
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
        // （编辑上限内进编辑窗、超编辑但在查看上限内进只读窗、图片走贴图窗、其余走系统），
        // 内容搜索命中时带着行号定位（只读窗不支持行号定位，命中行号仅编辑窗用）。
        // 只有超查看上限或不存在才落到下面的 OpenFile 系统链。
        // 右键「用系统打开」(OpenFile) 是纯系统打开，不走这里。
        if (action is "OpenDefault" or "OpenEditor")
        {
            if (File.Exists(fullPath) && TextFileOpenPolicy.IsSupported(fullPath))
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