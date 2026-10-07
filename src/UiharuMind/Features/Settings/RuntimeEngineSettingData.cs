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
using System.Linq;
using System.Text.RegularExpressions;
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
using UiharuMind.Core.Core.DownloadHelper;
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

    /// <summary>llama-server 专有启动选项</summary>
    public LLamaCppServerSettingsViewData Server { get; } = new();

    /// <summary>引擎卡上的版本号；没装引擎时说明一句</summary>
    public string EngineVersionText => SelectedVersion?.Name ?? Loc.Text(LangKey.LLamaCppNoEngine);

    /// <summary>当前版本是本机推荐的变体</summary>
    public bool IsEngineRecommended => SelectedVersion?.IsRecommended == true;

    /// <summary>当前版本的发布页，从版本名里认出构建号（b1234）</summary>
    public string? EngineReleaseUrl => SelectedVersion == null ? null : LLamaCppReleaseUrl(SelectedVersion.Name);

    /// <summary>比本机新的推荐引擎包；没有为 null</summary>
    public VersionInfo? EngineUpdate => LLamaCppEngineInstaller.Shared.AvailableUpdate;

    /// <summary>有新版本可更新</summary>
    public bool HasEngineUpdate => EngineUpdate != null;

    /// <summary>引擎卡上的新版本提示</summary>
    public string EngineUpdateText => EngineUpdate == null ? "" : Loc.Text(LangKey.LLamaCppUpdateAvailable, EngineUpdate.Name);

    /// <summary>下载源一节</summary>
    public DownloadSourceSettingsViewData DownloadSource { get; } = new();

    /// <summary>引擎更新通道：预览版几乎天天有新构建，正式版只跟正式版钉住的构建</summary>
    public IReadOnlyList<SettingChoice<bool>> EngineChannelOptions { get; } =
    [
        new(false, Loc.Text(LangKey.LLamaCppChannelPreview)),
        new(true, Loc.Text(LangKey.LLamaCppChannelStable))
    ];

    //上一次本地版本列表，用于更新时差分删除
    private List<VersionInfo> _lastAvailableVersions = new List<VersionInfo>();

    [ObservableProperty] private VersionInfo? _selectedVersion;
    [ObservableProperty] private SettingChoice<bool> _selectedEngineChannel;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private string? _updatedResutInfo;

    public RuntimeEngineSettingData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public RuntimeEngineSettingData(IMessageService messageService)
    {
        _messageService = messageService;
        _selectedEngineChannel = EngineChannelOptions.First(x => x.Value == LLamaCppSettingConfig.Current.UseStableChannel);
        RemoteDwnloadListViewModel = new DownloadListViewData(messageService)
        {
            DownloadCompletedHandler = OnRuntimeEngineDownloadCompleted,
            DeleteConfirmMessageProvider = () => Loc.Text(LangKey.ConfirmDeleteRuntimeEngine)
        };
        _ = InitializeAvailableVersions();
        RemoteDwnloadListViewModel.OnDownloadFileChange += () => _ = InitializeAvailableVersions();
        // 获取模型那边自动装的引擎也要出现在这里的版本下拉里
        LLamaCppEngineInstaller.Shared.Installed += installed => Dispatcher.UIThread.Post(() => _ = InitializeAvailableVersions());
        LLamaCppEngineInstaller.Shared.AvailableUpdateChanged += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(EngineUpdate));
            OnPropertyChanged(nameof(HasEngineUpdate));
            OnPropertyChanged(nameof(EngineUpdateText));
        });
    }

    // 与获取模型自动装引擎同一条路：排进全局下载队列，装好后选中
    [RelayCommand]
    private void InstallEngineUpdate()
    {
        if (EngineUpdate is not { } version) return;
        DownloadJob job = LLamaCppEngineInstaller.Shared.Enqueue(version, true);
        _messageService.ShowNotification(Loc.Text(LangKey.LLamaCppUpdateQueued, version.Name));
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DownloadJob.State)) return;
            if (job.State == EDownloadJobState.Completed)
                UiDispatcher.FireAndForget(() => _messageService.ShowNotification(
                    Loc.Text(LangKey.ModelDownloadEngineInstalled, version.Name), severity: MessageSeverity.Success));
            else if (job.State == EDownloadJobState.Failed)
                UiDispatcher.FireAndForget(() => _messageService.ShowNotification(
                    Loc.Text(LangKey.ModelDownloadEngineFailed, version.Name, job.Error?.Message ?? ""),
                    severity: MessageSeverity.Error));
        };
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
        LLamaCppEngineInstaller.Shared.ApplyVersions(versions.VersionsList);
    }

    [RelayCommand]
    private async Task ResetRuntimeSettings()
    {
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.LLamaCppResetAllConfirm))) return;
        RuntimeSettings.ResetToDefaults();
        Server.ResetToDefaults();
    }

    /// <summary>
    /// 从引擎版本名里认出构建号，拼 GitHub 发布页
    /// </summary>
    /// <param name="versionName">版本名，如 llama-b11443-bin-macos-arm64</param>
    /// <returns>发布页地址；认不出为 null</returns>
    public static string? LLamaCppReleaseUrl(string versionName)
    {
        Match match = BuildTagPattern().Match(versionName);
        return match.Success ? $"https://github.com/ggml-org/llama.cpp/releases/tag/{match.Value}" : null;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])b\d+(?![A-Za-z0-9])")]
    private static partial Regex BuildTagPattern();

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

    partial void OnSelectedEngineChannelChanged(SettingChoice<bool> value)
    {
        LLamaCppSettingConfig.Current.UseStableChannel = value.Value;
        LLamaCppSettingConfig.Current.Save();
    }

    partial void OnSelectedVersionChanged(VersionInfo? oldValue, VersionInfo? newValue)
    {
        OnPropertyChanged(nameof(EngineVersionText));
        OnPropertyChanged(nameof(IsEngineRecommended));
        OnPropertyChanged(nameof(EngineReleaseUrl));
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
