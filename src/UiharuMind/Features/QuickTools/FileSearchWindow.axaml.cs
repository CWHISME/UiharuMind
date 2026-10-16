using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.QuickTools;

/// <summary>
/// 现在也可以称为 文件浏览器
/// </summary>
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
        ViewModel.Breadcrumb.CollectionChanged += (_, _) => ScrollBreadcrumbToEnd();
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        this.SetScreenCenterPosition();
        ScrollBreadcrumbToEnd();
    }

    // 面包屑超长时左侧被截：滚动条藏了（Hidden 照样能滚），段一变就滚到底，
    // 永远露出最右侧的当前目录。排版没好时 Extent 还是旧的，丢到 Loaded 那一拍再滚
    private void ScrollBreadcrumbToEnd()
    {
        var scroller = this.FindControl<ScrollViewer>("BreadcrumbScroller");
        if (scroller == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            scroller.Offset = new Vector(
                Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width), 0);
        }, DispatcherPriority.Loaded);
    }

    // 点面包屑任一段回退到那个父目录（DataContext 是 DirSegment；末级的当前目录不跳）
    private void OnBreadcrumbSegmentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is UiharuMind.Features.QuickTools.DirSegment segment)
        {
            if (segment.IsCurrent) return;
            ViewModel.NavigateToSegmentCommand.Execute(segment);
        }
    }

    // 历史下拉的直达框：粘路径回车即跳
    private void OnHistoryJumpKeyDown(object? sender, KeyEventArgs e)
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

    // 历史下拉点一条切过去（DataContext 是 SidebarEntry）
    private void OnHistoryItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is SidebarEntry entry
            && entry.Path != ViewModel.CurrentDirectory)
        {
            ViewModel.SetDirectoryCommand.Execute(entry.Path);
        }
    }

    // 历史下拉的 ✕：从最近中移除（DataContext 是 SidebarEntry）
    private void OnHistoryForgetClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is SidebarEntry entry)
        {
            ViewModel.RemoveRecentCommand.Execute(entry);
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
            if (menuItem.Tag?.ToString() == "CopyPath")
            {
                App.Clipboard.CopyToClipboard(GetFullPath(item), true, true);
                return;
            }

            OpenTarget(item, menuItem.Tag?.ToString());
        }
    }

    // 主列双击统一入口（浏览/搜索两份列表共用）：目录下钻，文件打开。
    // 条目取自点中处所在的 ListBoxItem.DataContext，不赌选中时序
    private void OnMainDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source
            && source.FindAncestorOfType<ListBoxItem>()?.DataContext is SearchItem item)
        {
            if (item.IsDirectory)
                ViewModel.EnterDirectoryCommand.Execute(item);
            else
                OpenTarget(item);
        }
    }

    // 侧栏点选即跳（与 Finder 一致）。选中与当前目录相同时是外部导航回写的，不处理
    private void OnFavoriteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is SidebarEntry entry
            && entry.Path != ViewModel.CurrentDirectory)
        {
            ViewModel.SetDirectoryCommand.Execute(entry.Path);
        }
    }

    // 侧栏右键：揭示目录 / 复制路径 / 取消收藏（Tag 区分）
    private void OnSidebarMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not SidebarEntry entry) return;
        switch (menuItem.Tag?.ToString())
        {
            case "OpenDir":
                FileOpener.RevealInFolder(entry.Path);
                break;
            case "CopyPath":
                App.Clipboard.CopyToClipboard(entry.Path, true, true);
                break;
            case "RemoveFav":
                ViewModel.RemoveFavoriteCommand.Execute(entry);
                break;
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

    // 搜索/浏览命中的是相对搜索根的路径（根外的是绝对路径），拼回完整路径
    private string GetFullPath(SearchItem item) =>
        Path.IsPathFullyQualified(item.Path)
            ? Path.GetFullPath(item.Path)
            : Path.GetFullPath(Path.Combine(ViewModel.CurrentDirectory, item.Path));

    private async void OpenTarget(SearchItem item, string? tag = null)
    {
        var fullPath = GetFullPath(item);

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