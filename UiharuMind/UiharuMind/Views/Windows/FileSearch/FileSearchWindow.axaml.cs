using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using UiharuMind.Services;
using UiharuMind.ViewModels;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class FileSearchWindow : UiharuWindowBase
{
    public override bool IsCacheWindow => true;

    public SearchViewModel ViewModel => (SearchViewModel)DataContext!;

    public FileSearchWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<SearchViewModel>();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        ViewModel.Initialize();
    }

    private void OnDirectorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is string path)
        {
            ViewModel.SetDirectoryCommand.Execute(path);
        }
    }

    private void OnDirectoryLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            var text = comboBox.Text;
            if (!string.IsNullOrEmpty(text) && text != ViewModel.CurrentDirectory)
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

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source)
        {
            var listBox = source.FindAncestorOfType<ListBox>();
            if (listBox?.SelectedItem is SearchItem item)
            {
                var dir = Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    App.FilesService.OpenFolder(dir);
                }
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
}
