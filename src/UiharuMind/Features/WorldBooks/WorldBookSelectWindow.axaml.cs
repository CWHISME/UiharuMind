using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.WorldSettings;

namespace UiharuMind.Features.WorldBooks;

/// <summary>
/// 世界书选择窗：列出全部共享世界书，选中即回调；也能在这里新建一本并直接挂载
/// </summary>
public partial class WorldBookSelectWindow : Window
{
    private readonly Action<string>? _onSelectName;
    private readonly string _selectedName;

    public WorldBookSelectWindow(Action<string>? onSelectName, string selectedName)
    {
        InitializeComponent();
        _onSelectName = onSelectName;
        _selectedName = selectedName;
        RefreshBooks();
    }

    private void RefreshBooks()
    {
        BookList.ItemsSource = WorldSettingManager.Instance.GetOrderedItems()
            .Select(book => new WorldBookListItem(
                book.Name,
                $"{book.Entries.Count} 条目 · 预算 {book.TokenBudget}",
                book.Name == _selectedName))
            .ToList();
        BookList.SelectedItem = BookList.ItemsSource.Cast<WorldBookListItem>()
            .FirstOrDefault(item => item.IsSelected);
    }

    private void OnChooseClick(object? sender, RoutedEventArgs e)
    {
        if (BookList.SelectedItem is not WorldBookListItem item) return;
        _onSelectName?.Invoke(item.Name);
        Close();
    }

    private void OnCreateNewClick(object? sender, RoutedEventArgs e)
    {
        string name = NewBookName.Text?.Trim() ?? "";
        if (name.Length == 0) return;

        WorldSettingManager.Instance.Merge(name, []);
        _onSelectName?.Invoke(name);
        Close();
    }
}

/// <summary>选择窗列表项</summary>
/// <param name="Name">书名</param>
/// <param name="Summary">「N 条目 · 预算 X」</param>
/// <param name="IsSelected">是否当前已挂载的那本</param>
public sealed record WorldBookListItem(string Name, string Summary, bool IsSelected);