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
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;
using UiharuMind.Views;
using Ursa.Controls;

namespace UiharuMind.ViewModels.ViewData.Download;

/// <summary>
/// 下载列表视图模型基类
/// </summary>
public partial class DownloadListViewData : ObservableObject
{
    /// <summary>
    /// 添加删除请不要直接调用列表的清理，而是调用Clear方法(或者自己挨个调用每个 DownloadableItemData 元素的 Dispose 方法)
    /// </summary>
    public ObservableCollection<DownloadableItemData> RemoteVersions { get; set; } =
        new ObservableCollection<DownloadableItemData>();

    /// <summary>
    /// 用于索引下载对象，key为下载链接
    /// </summary>
    private Dictionary<string, DownloadableItemData> DownloadableItemsDictionary { get; } =
        new Dictionary<string, DownloadableItemData>();

    /// <summary>
    /// 当下载了新文件、或者删除了旧文件后，会调用
    /// </summary>
    public event Action? OnDownloadFileChange;

    /// <summary>
    /// 清理所有下载列表，如果想保留正在下载的对象，请调用 ClearIfNotDownloading
    /// </summary>
    public void ClearAll()
    {
        foreach (var item in RemoteVersions)
        {
            item.Dispose();
        }

        RemoteVersions.Clear();
        DownloadableItemsDictionary.Clear();
    }

    /// <summary>
    ///只清理没有正在下载的对象
    /// </summary>
    public void ClearIfNotDownloading()
    {
        List<DownloadableItemData> toDelete = new List<DownloadableItemData>();
        foreach (var item in RemoteVersions)
        {
            if (item.IsDownloading) continue;
            toDelete.Add(item);
        }

        foreach (var item in toDelete)
        {
            RemoveItem(item);
        }
    }

    /// <summary>
    /// 清理下载列表，保留正在下载的对象、以及下载完成的对象
    /// </summary>
    public void ClearIfNotExists()
    {
        List<DownloadableItemData> toDelete = new List<DownloadableItemData>();
        foreach (var item in RemoteVersions)
        {
            if (item.IsDownloading) continue;
            if (item.IsDownloaded) continue;
            toDelete.Add(item);
        }

        foreach (var item in toDelete)
        {
            item.Dispose();
            RemoteVersions.Remove(item);
            DownloadableItemsDictionary.Remove(item.Name);
        }
    }

    /// <summary>
    /// 是否存在某个下载对象
    /// </summary>
    /// <param name="itemName"></param>
    /// <returns></returns>
    public bool IsExists(string? itemName)
    {
        if (itemName == null) return false;
        return DownloadableItemsDictionary.ContainsKey(itemName);
    }

    /// <summary>
    /// 添加一个可以下载的对象
    /// </summary>
    /// <param name="item"></param>
    public void AddItem(DownloadableItemData item)
    {
        if (DownloadableItemsDictionary.ContainsKey(item.Name)) return;
        RemoteVersions.Add(item);
        DownloadableItemsDictionary.Add(item.Name, item);
    }

    /// <summary>
    /// 删除一个可以下载的对象
    /// </summary>
    /// <param name="item"></param>
    public void RemoveItem(DownloadableItemData item)
    {
        item.Dispose();
        RemoteVersions.Remove(item);
        DownloadableItemsDictionary.Remove(item.Name);
    }

    [RelayCommand]
    protected void OpenFolder(DownloadableItemData version)
    {
        App.FilesService.OpenFolder(GetLocalFileDirectoryPath(version));
    }

    [RelayCommand]
    protected void DownloadVersion(DownloadableItemData version)
    {
        // SimpleZipDownloader downloader1 = new SimpleZipDownloader();
        // downloader1.DownloadProgressChanged += (totalRead, totalBytes) =>
        // {
        //     if (totalBytes != -1)
        //     {
        //         Log.Debug($"下载进度1: {totalRead}/{totalBytes} ({(double)totalRead / totalBytes:P})");
        //     }
        //     else
        //     {
        //         Log.Debug($"下载进度1: {totalRead} bytes");
        //     }
        // };
        //
        // downloader1.ExtractProgressChanged += (progress) => { Log.Debug($"解压进度1: {progress}"); };
        //
        // await downloader1.DownloadAndExtractAsync(version.DownloadUrl, version.ExecutablePath);
        version.StartDownload(OnDownloadCompleted);
    }

    [RelayCommand]
    protected async Task DeleteFile(DownloadableItemData version)
    {
        try
        {
            var result =
                await App.MessageService.ShowConfirmMessageBox(Lang.ConfirmDeleteRuntimeEngine,
                    UIManager.GetRootWindow());
            if (result == MessageBoxResult.Yes)
            {
                var path = GetDeleteFilePath(version);
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path, true);
                RemoveItem(version);
            }

            OnDownloadFileChange?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }


    /// <summary>
    /// 点击打开文件所在的文件夹按钮时调用
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    protected virtual string? GetLocalFileDirectoryPath(DownloadableItemData version)
    {
        return Path.GetDirectoryName(version.DownloadFilePath);
    }

    /// <summary>
    /// 获取删除文件的路径
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    protected virtual string GetDeleteFilePath(DownloadableItemData version)
    {
        return version.DownloadFilePath;
    }

    /// <summary>
    /// 当文件下载完毕时调用
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    protected virtual Task OnFileDownloadCompleted(DownloadableItemData version)
    {
        return Task.CompletedTask;
    }

    private async void OnDownloadCompleted(DownloadableItemData obj)
    {
        await OnFileDownloadCompleted(obj);

        OnDownloadFileChange?.Invoke();
    }
}