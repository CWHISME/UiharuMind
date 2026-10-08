using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本窗的交互：标题栏按钮、「更多」菜单、快捷键与拖放
/// </summary>
public partial class TextFileWindow
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+F / Cmd+F：编辑模式下编辑器自己的 SearchPanel 先处理（Handled 后到不了这里）；
        // 到这说明是预览态或编辑器没接住——切回原文再开搜索。
        // macOS 的搜索/打开/保存习惯是 Cmd（KeyModifiers.Meta），所以 Ctrl 和 Meta 都认
        var cmd = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta);
        if (e.Key == Key.N && cmd != 0)
        {
            _ = NewEmptyAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && cmd != 0)
        {
            DocumentView.OpenSearch();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && cmd != 0)
        {
            _ = OpenFileAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.S && cmd != 0)
        {
            _ = TrySaveAsync();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>整窗拖放（对照 ConversationView）：只在文件拖放时介入并亮起蒙版</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool isFile = e.DataTransfer.Formats.Any(f => f == DataFormat.File);
        DropOverlay.IsVisible = isFile;
        if (!isFile) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave 是冒泡路由事件：指针在窗内跨过子控件边界也会冒上来，
        // 只有真的离开整个窗口才收起蒙版，否则内部移动会反复闪烁
        Point p = e.GetPosition(this);
        if (p.X < 0 || p.Y < 0 || p.X > Bounds.Width || p.Y > Bounds.Height)
            DropOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (!e.DataTransfer.Formats.Any(f => f == DataFormat.File)) return;

        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(DataFormat.File) is not IStorageItem storageItem) continue;
            string? path = storageItem.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;

            // 一次只开一个：编辑窗一份只装一份文档。二进制/超大由 LoadFileAsync 转交系统
            await LoadFileAsync(path);
            return;
        }

        e.Handled = true;
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        DocumentView.TextView.CopyAll();
    }

    private void WordWrapButton_Click(object? sender, RoutedEventArgs e)
    {
        _setting.WordWrap = WordWrapButton.IsChecked == true;
        _setting.Save();
    }

    private void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        DocumentView.OpenSearch();
    }

    private void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        // 用户主动点预览：立即渲染，不等视口队列
        DocumentView.ShowPreview(PreviewButton.IsChecked == true);
    }

    /// <summary>
    /// 「更多」每次展开时重建「最近打开」（顺手剔除已删除的文件），撤回 / 重做能不能点也在这时对一遍。
    /// 入口挂在「更多」上而不是最近子菜单上：空子菜单根本打不开，它自己的展开事件永不触发。
    /// 动态条目不用 loc 绑定：语言切换后下次展开即按新语言重建
    /// </summary>
    private void MoreMenu_Opening(object? sender, EventArgs e)
    {
        RefreshMenuStates();
        _setting.PruneMissingFiles();
        RecentMenuItem.Items.Clear();

        if (_setting.RecentFiles.Count == 0)
        {
            RecentMenuItem.Items.Add(new MenuItem
            {
                Header = Loc.Text(LangKey.TextFileMenuRecentEmpty),
                IsEnabled = false
            });
            return;
        }

        foreach (string path in _setting.RecentFiles)
        {
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            string captured = path;
            item.Click += async (_, _) => await LoadFileAsync(captured);
            RecentMenuItem.Items.Add(item);
        }

        RecentMenuItem.Items.Add(new Separator());
        var clear = new MenuItem { Header = Loc.Text(LangKey.TextFileMenuRecentClear) };
        clear.Click += (_, _) => _setting.ClearRecentFiles();
        RecentMenuItem.Items.Add(clear);
    }

    private async void NewMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await NewEmptyAsync();
    }

    private async void OpenMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await OpenFileAsync();
    }

    /// <summary>打开：不过滤后缀（任意文件可选），准入由 LoadFileAsync 统一判定</summary>
    private async Task OpenFileAsync()
    {
        var file = await App.FilesService.OpenFileAsync(this, null, "*");
        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return; //用户取消

        await LoadFileAsync(path);
    }

    private async void SaveAsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await SaveAsAsync();
    }

    private async void SaveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await TrySaveAsync();
    }

    private void UndoMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        DocumentView.TextView.Undo();
    }

    private void RedoMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        DocumentView.TextView.Redo();
    }

    private void LineNumbersMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        LineNumbersPreference = LineNumbersMenuItem.IsChecked;
        _setting.Save();
    }

    private void SyntaxMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        // 关掉时不高亮；重新打开能恢复（TextMate 认不出扩展名也会自然退化成纯文本）
        _setting.SyntaxHighlight = SyntaxMenuItem.IsChecked;
        _setting.Save();
    }
}
