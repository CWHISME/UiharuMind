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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Shared.Data;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

public partial class RuntimeEngineSettingData : ObservableObject
{
    private readonly IMessageService _messageService;
    public ObservableCollection<VersionInfo?> AvailableVersions { get; set; } =
        new ObservableCollection<VersionInfo?>();

    public DownloadListViewData RemoteDwnloadListViewModel { get; }
    public ModelRuntimeBasicSettingsData RuntimeSettings { get; } = new();

    /// <summary>下载源一节</summary>
    public DownloadSourceSettingsViewData DownloadSource { get; } = new();

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
            DownloadCompletedHandler = OnRuntimeEngineDownloadCompleted,
            DeleteConfirmMessageProvider = () => Loc.Text(LangKey.ConfirmDeleteRuntimeEngine)
        };
        _ = InitializeAvailableVersions();
        RemoteDwnloadListViewModel.OnDownloadFileChange += () => _ = InitializeAvailableVersions();
        // 获取模型那边自动装的引擎也要出现在这里的版本下拉里
        LLamaCppEngineInstaller.Shared.Installed += installed => Dispatcher.UIThread.Post(() => _ = InitializeAvailableVersions());
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
        await AsyncCommandScope.RunAsync(
            v => IsCheckingForUpdate = v,
            PullRemoteVersionsAsync,
            e => UpdatedResutInfo = e.Message, //拉取失败就把原因写在结果那一行，界面本来就在显示它
            IsCheckingForUpdate);
    }

    private async Task PullRemoteVersionsAsync()
    {
        RemoteDwnloadListViewModel.ClearIfNotExists();
        var versions = await LlmManager.Instance.PullLatestRuntimeVersion();
        foreach (var version in versions.VersionsList)
        {
            if (RemoteDwnloadListViewModel.IsExists(version.Name)) continue;
            RemoteDwnloadListViewModel.AddItem(new DownloadableItemData(version, true));
        }

        UpdatedResutInfo = versions.ReleaseDate;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        App.FilesService.OpenFolder(AppPaths.External.Engine);
    }

    private async Task InitializeAvailableVersions()
    {
        // AvailableVersions.Clear();
        var versions = await LlmManager.Instance.GetLocalRuntimeVersions();
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

        SelectedVersion = LlmManager.Instance.CurrentRuntimeVersion;
    }

    partial void OnSelectedVersionChanged(VersionInfo? oldValue, VersionInfo? newValue)
    {
        if (newValue == null)
        {
            SelectedVersion = LlmManager.Instance.CurrentRuntimeVersion;
            return;
        }

        if (oldValue != null) LlmManager.Instance.SetSelectedRuntimeVersion(newValue);
    }

    private static async Task OnRuntimeEngineDownloadCompleted(DownloadableItemData item)
    {
        // 与获取模型自动装引擎走同一条安装：解压到临时目录、校验有 llama-server 再换上
        var version = (VersionInfo)item.Target;
        item.IsDownloading = true;
        item.DownloadInfo = Loc.Text(LangKey.Decompressing) + item.DownloadInfo;
        try
        {
            await LLamaCppEngineInstaller.Shared.InstallAsync(version);
        }
        catch (Exception e)
        {
            Log.Error($"Install runtime engine failed: {version.Name}, {e.Message}");
            item.ErrorMessage = e.Message;
        }

        item.IsDownloading = false;
        item.InitFileSize();
    }
}
