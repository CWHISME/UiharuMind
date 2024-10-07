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
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Versions;
using UiharuMind.ViewModels.ViewData.Download;

namespace UiharuMind.ViewModels.SettingViewData;

public partial class RuntimeEngineSettingModel : ObservableObject
{
    public ObservableCollection<VersionInfo?> AvailableVersions { get; set; } =
        new ObservableCollection<VersionInfo?>();

    public RuntimeEngineDownloadListViewModel RemoteDwnloadListViewModel { get; set; } =
        new RuntimeEngineDownloadListViewModel();

    //上一次本地版本列表，用于更新时差分删除
    private List<VersionInfo> _lastAvailableVersions = new List<VersionInfo>();

    [ObservableProperty] private VersionInfo? _selectedVersion;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private string? _updatedResutInfo;

    public RuntimeEngineSettingModel()
    {
        InitializeAvailableVersions();
        RemoteDwnloadListViewModel.OnDownloadFileChange += InitializeAvailableVersions;
    }

    private async void InitializeAvailableVersions()
    {
        // AvailableVersions.Clear();
        var versions = await LlmManager.Instance.RuntimeEngineManager.GetLocalVersions();
        foreach (var version in versions.VersionsList)
        {
            _lastAvailableVersions.Remove(version);
            if (AvailableVersions.IndexOf(version) > -1) continue;
            AvailableVersions.Add(version);
            RemoteDwnloadListViewModel.AddItem(new DownloadableItemData(version, true));
        }

        //删除本地版本列表中不存在的版本
        foreach (var item in _lastAvailableVersions)
        {
            AvailableVersions.Remove(item);
        }

        //缓存本地版本列表
        _lastAvailableVersions.AddRange(versions.VersionsList);

        SelectedVersion = LlmManager.Instance.RuntimeEngineManager.CurrentSeletedVersion;
    }

    [RelayCommand]
    public async Task UpdateRemoteVersions()
    {
        IsCheckingForUpdate = true;
        RemoteDwnloadListViewModel.ClearIfNotExists();
        var versions = await LlmManager.Instance.RuntimeEngineManager.PullLastestVersion();
        foreach (var version in versions.VersionsList)
        {
            if (RemoteDwnloadListViewModel.IsExists(version.Name)) continue;
            RemoteDwnloadListViewModel.AddItem(new DownloadableItemData(version, true));
        }

        UpdatedResutInfo = versions.ReleaseDate;
        IsCheckingForUpdate = false;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        App.FilesService.OpenFolder(SettingConfig.BackendRuntimeEnginePath);
    }

    //
    // [RelayCommand]
    // private void DownloadVersion(DownloadableItemData version)
    // {
    //     // SimpleZipDownloader downloader1 = new SimpleZipDownloader();
    //     // downloader1.DownloadProgressChanged += (totalRead, totalBytes) =>
    //     // {
    //     //     if (totalBytes != -1)
    //     //     {
    //     //         Log.Debug($"下载进度1: {totalRead}/{totalBytes} ({(double)totalRead / totalBytes:P})");
    //     //     }
    //     //     else
    //     //     {
    //     //         Log.Debug($"下载进度1: {totalRead} bytes");
    //     //     }
    //     // };
    //     //
    //     // downloader1.ExtractProgressChanged += (progress) => { Log.Debug($"解压进度1: {progress}"); };
    //     //
    //     // await downloader1.DownloadAndExtractAsync(version.DownloadUrl, version.ExecutablePath);
    //     version.StartDownload(OnDownloadCompleted);
    // }
    //
    // [RelayCommand]
    // private async Task DeleteVersion(DownloadableItemData version)
    // {
    //     try
    //     {
    //         var result = await App.MessageService.ShowConfirmMessage(Lang.ConfirmDeleteRuntimeEngine);
    //         if (result == MessageBoxResult.Yes)
    //         {
    //             var ver = (VersionInfo)version.Target;
    //             Directory.Delete(ver.ExecutablePath, true);
    //         }
    //     }
    //     catch (Exception e)
    //     {
    //         Log.Error(e.Message);
    //     }
    // }
    //
    // private async void OnDownloadCompleted(DownloadableItemData obj)
    // {
    //     //解压完成后，刷新版本列表
    //     var version = (VersionInfo)obj.Target;
    //     obj.IsDownloading = true;
    //     obj.DownloadInfo = "[解压中...]" + obj.DownloadInfo;
    //     await SimpleZipHelper.ExtractZipFile(obj.DownloadFilePath, version.ExecutablePath, true);
    //     obj.IsDownloading = false;
    //     InitializeAvailableVersions();
    // }

    // partial void OnSelectedIndexChanged(int oldValue, int newValue)
    // {
    //     
    // }

    partial void OnSelectedVersionChanged(VersionInfo? oldValue, VersionInfo? newValue)
    {
        if (newValue == null)
        {
            SelectedVersion = LlmManager.Instance.RuntimeEngineManager.CurrentSeletedVersion;
            return;
        }

        if (oldValue != null) LlmManager.Instance.RuntimeEngineManager.SetSelectedVersion(newValue);
    }
}