using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Services;

public class FilesService //: IStorageFolder
{
    private readonly Window _target;
    private readonly FolderPickerOpenOptions _filePickerOption;

    public FilesService(Window target)
    {
        _target = target;
        _filePickerOption = new FolderPickerOpenOptions();
        // _filePickerOption.SuggestedStartLocation = this;
    }

    public async Task<string> OpenSelectFolderAsync(string defaultPath)
    {
        // _filePickerOption.Title = "Select Folder";
        // _path = new Uri(defaultPath);
        var folder = await _target.StorageProvider.TryGetFolderFromPathAsync(new Uri(defaultPath));
        _filePickerOption.SuggestedStartLocation = folder;
        var result = await _target.StorageProvider.OpenFolderPickerAsync(_filePickerOption);
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

    public async Task<IStorageFile?> OpenFileAsync()
    {
        var files = await _target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open File",
        });

        return files.Count >= 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveFileAsync()
    {
        return await _target.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save File"
        });
    }
}