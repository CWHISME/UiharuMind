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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Versions;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.ViewModels.ViewData.Download;

namespace UiharuMind.ViewModels.SettingViewData;

public partial class RuntimeEngineSettingData : ObservableObject
{
    private readonly IMessageService _messageService;
    public ObservableCollection<VersionInfo?> AvailableVersions { get; set; } =
        new ObservableCollection<VersionInfo?>();

    public DownloadListViewData RemoteDwnloadListViewModel { get; }

    //上一次本地版本列表，用于更新时差分删除
    private List<VersionInfo> _lastAvailableVersions = new List<VersionInfo>();

    [ObservableProperty] private VersionInfo? _selectedVersion;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private string? _updatedResutInfo;

    public RuntimeEngineSettingData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public RuntimeEngineSettingData(IMessageService messageService)
    {
        _messageService = messageService;
        RemoteDwnloadListViewModel = new DownloadListViewData(messageService)
        {
            DeleteFilePathProvider = item => ((VersionInfo)item.Target).ExecutablePath,
            LocalFileDirectoryPathProvider = item => ((VersionInfo)item.Target).ExecutablePath,
            DownloadCompletedHandler = OnRuntimeEngineDownloadCompleted,
            DeleteConfirmMessageProvider = () => Lang.ConfirmDeleteRuntimeEngine
        };
        _ = InitializeAvailableVersions();
        RemoteDwnloadListViewModel.OnDownloadFileChange += () => _ = InitializeAvailableVersions();
    }

    [RelayCommand]
    private async Task ReloadRuntimeEngineList()
    {
        await InitializeAvailableVersions();
        _messageService.ShowNotification("Engine list updated.");
    }

    [RelayCommand]
    private async Task UpdateRemoteVersions()
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

    private async Task InitializeAvailableVersions()
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

    partial void OnSelectedVersionChanged(VersionInfo? oldValue, VersionInfo? newValue)
    {
        if (newValue == null)
        {
            SelectedVersion = LlmManager.Instance.RuntimeEngineManager.CurrentSeletedVersion;
            return;
        }

        if (oldValue != null) LlmManager.Instance.RuntimeEngineManager.SetSelectedVersion(newValue);
    }

    private static async Task OnRuntimeEngineDownloadCompleted(DownloadableItemData item)
    {
        // runtime engine 发布包是 zip，下载完成后解压到版本目录。
        var version = (VersionInfo)item.Target;
        item.IsDownloading = true;
        item.DownloadInfo = Lang.Decompressing + item.DownloadInfo;
        await SimpleZipHelper.ExtractZipFile(item.DownloadFilePath, version.ExecutablePath, true);
        item.IsDownloading = false;
        item.InitFileSize();
    }
}
