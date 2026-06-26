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
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;

namespace UiharuMind.ViewModels.ViewData.Download;

/// <summary>
/// 下载列表视图模型基类
/// </summary>
public partial class DownloadListViewData : ObservableObject
{
    protected readonly IMessageService MessageService;
    private string _downloadedActionText = Lang.OpenDirectory;

    public DownloadListViewData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public DownloadListViewData(IMessageService messageService)
    {
        MessageService = messageService;
    }

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

    public string DownloadedActionText
    {
        get => _downloadedActionText;
        set => SetProperty(ref _downloadedActionText, value);
    }

    public Func<DownloadableItemData, string?>? LocalFileDirectoryPathProvider { get; set; }
    public Func<DownloadableItemData, string>? DeleteFilePathProvider { get; set; }
    public Func<string>? DeleteConfirmMessageProvider { get; set; }
    public Func<DownloadableItemData, Task>? DownloadCompletedHandler { get; set; }
    public Func<DownloadableItemData, Task>? DownloadedActionHandler { get; set; }

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
    protected async Task OpenFolder(DownloadableItemData version)
    {
        if (DownloadedActionHandler != null)
        {
            await DownloadedActionHandler(version);
            return;
        }

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
            if (await MessageService.ConfirmAsync(GetDeleteConfirmMessage()))
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
    protected string? GetLocalFileDirectoryPath(DownloadableItemData version)
    {
        if (version.Target is IInstalledDownloadable installedDownloadable &&
            !string.IsNullOrWhiteSpace(installedDownloadable.InstalledPath) &&
            Directory.Exists(installedDownloadable.InstalledPath))
        {
            return installedDownloadable.InstalledPath;
        }

        return LocalFileDirectoryPathProvider?.Invoke(version) ??
               Path.GetDirectoryName(version.DownloadFilePath);
    }

    /// <summary>
    /// 获取删除文件的路径
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    protected string GetDeleteFilePath(DownloadableItemData version)
    {
        if (version.Target is IInstalledDownloadable installedDownloadable &&
            !string.IsNullOrWhiteSpace(installedDownloadable.InstalledPath) &&
            (File.Exists(installedDownloadable.InstalledPath) || Directory.Exists(installedDownloadable.InstalledPath)))
        {
            return installedDownloadable.InstalledPath;
        }

        return DeleteFilePathProvider?.Invoke(version) ?? version.DownloadFilePath;
    }

    protected string GetDeleteConfirmMessage()
    {
        return DeleteConfirmMessageProvider?.Invoke() ?? Lang.ConfirmDeleteRuntimeEngine;
    }

    protected void NotifyDownloadFileChanged()
    {
        OnDownloadFileChange?.Invoke();
    }

    /// <summary>
    /// 当文件下载完毕时调用
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    protected Task OnFileDownloadCompleted(DownloadableItemData version)
    {
        return DownloadCompletedHandler?.Invoke(version) ?? Task.CompletedTask;
    }

    private async void OnDownloadCompleted(DownloadableItemData obj)
    {
        await OnFileDownloadCompleted(obj);

        OnDownloadFileChange?.Invoke();
    }
}
