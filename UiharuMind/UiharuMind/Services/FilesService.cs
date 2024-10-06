using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views;

namespace UiharuMind.Services;

public class FilesService //: IStorageFolder
{
    // private readonly Window _target;
    private readonly FolderPickerOpenOptions _filePickerOption;

    public FilesService()
    {
        // _target = target;
        _filePickerOption = new FolderPickerOpenOptions();
        // _filePickerOption.SuggestedStartLocation = this;
    }

    public async Task<string> OpenSelectFolderAsync(string defaultPath, Window? owner = null)
    {
        // _filePickerOption.Title = "Select Folder";
        // _path = new Uri(defaultPath);
        if (owner == null) owner = UIManager.GetRootWindow();
        var folder = await owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(defaultPath));
        _filePickerOption.SuggestedStartLocation = folder;
        var result = await owner.StorageProvider.OpenFolderPickerAsync(_filePickerOption);
        return result.FirstOrDefault()?.TryGetLocalPath() ?? defaultPath;
    }

    public void OpenFolder(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Log.Error("Path is null or empty");
            return;
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        try
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public async Task<IStorageFile?> OpenFileAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open File",
        });

        return files.Count >= 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveFileAsync(Window owner)
    {
        return await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save File"
        });
    }
}