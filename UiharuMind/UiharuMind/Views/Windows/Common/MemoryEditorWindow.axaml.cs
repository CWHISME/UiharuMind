using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as MemoryEditorWindowModel)?.OnClose();
    }
}

public partial class MemoryEditorWindowModel : ObservableObject
{
    private MemoryData _memoryData;
    private Action? _onClose;
    private bool _isInitializing;

    public ObservableCollection<string> Texts { get; set; }
    public ObservableCollection<string> FilePaths { get; set; }
    public ObservableCollection<string> DirectoryPaths { get; set; }
    [ObservableProperty] private string _urlPaths;
    [ObservableProperty] private string _indexStatusText = "";
    [ObservableProperty] private bool _isIndexUpdating;

    public MemoryEditorWindowModel()
    {
        _isInitializing = true;
        _memoryData = new MemoryData();
        Texts = new ObservableCollection<string>();
        FilePaths = new ObservableCollection<string>();
        DirectoryPaths = new ObservableCollection<string>();
        UrlPaths = "";
        _isInitializing = false;
        RefreshIndexStatus();
    }

    public MemoryEditorWindowModel(MemoryData memoryData, Action? onClose = null)
    {
        _isInitializing = true;
        _memoryData = memoryData;
        _onClose = onClose;
        Texts = new ObservableCollection<string>(memoryData.Texts);
        FilePaths = new ObservableCollection<string>(memoryData.FilePaths);
        DirectoryPaths = new ObservableCollection<string>(memoryData.DirectoryPaths);
        UrlPaths = string.Join(Environment.NewLine, memoryData.UrlPaths);
        _isInitializing = false;
        RefreshIndexStatus();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UpdateIndex()
    {
        if (IsIndexUpdating) return;
        IsIndexUpdating = true;
        RefreshIndexStatus();
        try
        {
            var success = await _memoryData.UpdateIndexAsync();
            if (success)
                App.MessageService.ShowNotification(Lang.MemoryIndexUpdateSuccess);
            else
                App.MessageService.ShowWarningMessageBox(BuildMemoryIndexFailureMessage(_memoryData.LastIndexError));
        }
        finally
        {
            IsIndexUpdating = false;
            RefreshIndexStatus();
        }
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
                RefreshIndexStatus();
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
                RefreshIndexStatus();
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
            RefreshIndexStatus();
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
                RefreshIndexStatus();
            });
    }

    public void OnClose()
    {
        _onClose?.Invoke();
    }

    private void ForceDeleteText(string text)
    {
        Texts.Remove(text);
        _memoryData.Texts.Remove(text);
        _memoryData.Save();
        RefreshIndexStatus();
    }

    private void ForceAddText(string text)
    {
        Texts.Add(text);
        _memoryData.Texts.Add(text);
        _memoryData.Save();
        RefreshIndexStatus();
    }

    partial void OnUrlPathsChanged(string value)
    {
        if (_isInitializing) return;
        _memoryData.UrlPaths =
            value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
        _memoryData.Save();
        RefreshIndexStatus();
    }

    private void RefreshIndexStatus()
    {
        if (IsIndexUpdating)
        {
            IndexStatusText = Lang.MemoryIndexUpdating;
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append(GetIndexStateText());
        if (_memoryData.LastIndexedAt != null)
            sb.Append(" ")
                .Append(Lang.MemoryIndexLastIndexed)
                .Append(_memoryData.LastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        if (!string.IsNullOrEmpty(_memoryData.LastIndexError))
            sb.Append(" ").Append(Lang.MemoryIndexLastError).Append(GetMemoryIndexErrorText(_memoryData.LastIndexError));
        IndexStatusText = sb.ToString();
    }

    private string GetIndexStateText()
    {
        if (_memoryData.IndexDirty) return Lang.MemoryIndexNeedUpdate;
        if (_memoryData.LastIndexedAt == null) return Lang.MemoryIndexNotBuilt;
        return Lang.MemoryIndexReady;
    }

    private static string BuildMemoryIndexFailureMessage(string error)
    {
        if (string.IsNullOrEmpty(error)) return Lang.MemoryIndexUpdateFailed;
        return Lang.MemoryIndexUpdateFailed + Environment.NewLine + GetMemoryIndexErrorText(error);
    }

    private static string GetMemoryIndexErrorText(string error)
    {
        return error switch
        {
            "Embedding server is unavailable." => Lang.MemoryIndexEmbeddingServerUnavailable,
            "Embedding server startup timed out." => Lang.MemoryIndexEmbeddingServerTimeout,
            "Memory name not set" => Lang.MemoryIndexMemoryNameMissing,
            "Memory vector store unavailable" => Lang.MemoryIndexVectorStoreUnavailable,
            "Memory index update failed" => Lang.MemoryIndexUpdateFailed,
            _ => error
        };
    }
}
