/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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

    public async Task<string> OpenSelectFolderAsync(string? defaultPath = null, Window? owner = null)
    {
        // _filePickerOption.Title = "Select Folder";
        // _path = new Uri(defaultPath);
        if (owner == null) owner = UIManager.GetFoucusWindow();
        if (defaultPath != null && Directory.Exists(defaultPath))
        {
            var folder = await owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(defaultPath)));
            _filePickerOption.SuggestedStartLocation = folder;
        }

        var result = await owner.StorageProvider.OpenFolderPickerAsync(_filePickerOption);
        return result.FirstOrDefault()?.TryGetLocalPath() ?? "";
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

    public async Task<IStorageFile?> OpenFileAsync(Window? owner, string? defaultPath = null,
        params string[] fileTypeFilter)
    {
        if (owner == null) owner = UIManager.GetFoucusWindow();
        FilePickerFileType fileType = new FilePickerFileType("Filter") { Patterns = fileTypeFilter };
        var defaultUri = string.IsNullOrEmpty(defaultPath)
            ? null
            : new Uri(Path.GetFullPath(defaultPath));
        var defaultLocation =
            defaultUri != null ? await owner.StorageProvider.TryGetFolderFromPathAsync(defaultUri) : null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open File",
            FileTypeFilter = new List<FilePickerFileType>() { fileType },
            SuggestedStartLocation = defaultLocation,
        });

        return files.Count >= 1 ? files[0] : null;
    }

    public async Task<IReadOnlyList<IStorageFile>> SelectFileAsync(Window? owner = null, string? defaultPath = null,
        params string[] fileTypeFilter)
    {
        if (owner == null) owner = UIManager.GetFoucusWindow();
        FilePickerFileType fileType = new FilePickerFileType("Filter") { Patterns = fileTypeFilter };
        var defaultUri = string.IsNullOrEmpty(defaultPath)
            ? null
            : new Uri(Path.GetFullPath(defaultPath));
        var defaultLocation =
            defaultUri != null ? await owner.StorageProvider.TryGetFolderFromPathAsync(defaultUri) : null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Select File",
            FileTypeFilter = new List<FilePickerFileType>() { fileType },
            SuggestedStartLocation = defaultLocation,
        });

        return files;
    }

    public async Task<Uri?> SaveFileAsync(Window owner, string defaultName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save File",
            SuggestedFileName = defaultName,
        });
        return file?.Path;
    }

    /// <summary>
    /// 显示保存图片对话框，直接存保存图片
    /// </summary>
    /// <param name="bitmap"></param>
    /// <param name="owner"></param>
    /// <param name="defaultName"></param>
    public async Task SaveImageAsync(Bitmap bitmap, Window? owner = null, string? defaultName = null)
    {
        if (owner == null) owner = UIManager.GetFoucusWindow();
        var path = await App.FilesService.SaveFileAsync(owner,
            defaultName ?? "Uiharu_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
        if (path == null) return;
        bitmap.Save(path.LocalPath);
    }
}