using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Views.Windows.Common;

public partial class MemoryEditorWindow : Window
{
    public MemoryEditorWindow()
    {
        InitializeComponent();
    }
}

public partial class MemoryEditorWindowModel : ObservableObject
{
    private MemoryData _memoryData;

    public ObservableCollection<string> Texts { get; set; }
    public ObservableCollection<string> FilePaths { get; set; }
    public ObservableCollection<string> DirectoryPaths { get; set; }
    [ObservableProperty] private string _urlPaths;

    public MemoryEditorWindowModel()
    {
        _memoryData = new MemoryData();
        Texts = new ObservableCollection<string>();
        FilePaths = new ObservableCollection<string>();
        DirectoryPaths = new ObservableCollection<string>();
        UrlPaths = "";
    }

    public MemoryEditorWindowModel(MemoryData memoryData)
    {
        _memoryData = memoryData;
        Texts = new ObservableCollection<string>(memoryData.Texts);
        FilePaths = new ObservableCollection<string>(memoryData.FilePaths);
        DirectoryPaths = new ObservableCollection<string>(memoryData.DirectoryPaths);
        UrlPaths = string.Join(Environment.NewLine, memoryData.UrlPaths);
    }

    [RelayCommand]
    private async Task AddText()
    {
        var text = await UIManager.ShowStringEditWindow("");
        if (!string.IsNullOrEmpty(text))
        {
            ForceAddText(text);
        }
    }

    [RelayCommand]
    private async Task EditText(string text)
    {
        var newText = await UIManager.ShowStringEditWindow(text);
        if (!string.IsNullOrEmpty(newText) && newText != text)
        {
            ForceDeleteText(text);
            ForceAddText(newText);
        }
    }

    [RelayCommand]
    private void DeletetText(string text)
    {
        App.MessageService.ShowConfirmMessageBox(Lang.CommonDeleteConfirmTips,
            () => { ForceDeleteText(text); });
    }

    [RelayCommand]
    private async Task AddFile()
    {
        var files = await App.FilesService.SelectFileAsync();
        if (files.Count > 0)
        {
            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(localPath)) continue;
                FilePaths.Add(localPath);
                _memoryData.FilePaths.Add(localPath);
                _memoryData.Save();
            }
        }
    }

    [RelayCommand]
    private void OpenFileFolder(string path)
    {
        App.FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path);
    }

    [RelayCommand]
    public void DeletetFile(string path)
    {
        App.MessageService.ShowConfirmMessageBox(Lang.CommonDeleteConfirmTips,
            () =>
            {
                FilePaths.Remove(path);
                _memoryData.FilePaths.Remove(path);
                _memoryData.Save();
            });
    }

    [RelayCommand]
    public async Task AddDirectory()
    {
        var directory = await App.FilesService.OpenSelectFolderAsync();
        if (!string.IsNullOrEmpty(directory))
        {
            DirectoryPaths.Add(directory);
            _memoryData.DirectoryPaths.Add(directory);
            _memoryData.Save();
        }
    }

    [RelayCommand]
    private void OpenDirecoryFolder(string path)
    {
        App.FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path);
    }

    [RelayCommand]
    public void DeletetDirectory(string directory)
    {
        App.MessageService.ShowConfirmMessageBox(Lang.CommonDeleteConfirmTips,
            () =>
            {
                DirectoryPaths.Remove(directory);
                _memoryData.DirectoryPaths.Remove(directory);
                _memoryData.Save();
            });
    }

    private void ForceDeleteText(string text)
    {
        Texts.Remove(text);
        _memoryData.Texts.Remove(text);
        _memoryData.Save();
    }

    private void ForceAddText(string text)
    {
        Texts.Add(text);
        _memoryData.Texts.Add(text);
        _memoryData.Save();
    }

    partial void OnUrlPathsChanged(string value)
    {
        _memoryData.UrlPaths =
            value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
        _memoryData.Save();
    }
}