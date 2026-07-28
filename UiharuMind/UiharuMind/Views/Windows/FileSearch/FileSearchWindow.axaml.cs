using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CliWrap;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
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

        tag ??= "OpenFile";
        switch (tag)
        {
            case "OpenFile":
                if (File.Exists(fullPath))
                {
                    if (PlatformUtils.IsMacOS)
                    {
                        var result = await Cli.Wrap("open")
                            .WithArguments($"\"{fullPath}\"")
                            // 设置为 None，这样当 ExitCode != 0 时，CliWrap 不会抛出异常，
                            .WithValidation(CommandResultValidation.None)
                            .ExecuteAsync();
                        if (result.ExitCode != 0) OpenTarget(item, "OpenDir");
                        return;
                    }

                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
                else
                    OpenTarget(item, "OpenDir");

                break;
            case "OpenDir":
                var dir = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
                if (Directory.Exists(dir))
                {
                    if (PlatformUtils.IsWindows)
                    {
                        Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                    }
                    else if (PlatformUtils.IsMacOS)
                    {
                        Process.Start("open", $"-R \"{fullPath}\"");
                    }
                    else App.FilesService.OpenFolder(dir);
                }
                else
                    App.Services.GetRequiredService<IMessageService>().ShowNotification("Directory not found");

                break;
        }
    }
}