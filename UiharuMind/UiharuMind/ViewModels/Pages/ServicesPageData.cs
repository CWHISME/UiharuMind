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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Views;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class ServicesPageData : PageDataBase
{
    private readonly IMessageService _messageService;
    private readonly EmbeddingModelService _embeddingService = EmbeddingModelService.Instance;
    private readonly EmbeddingModelSettingConfig _embeddingConfig = ConfigManager.Instance.EmbeddingModelSetting;
    private bool _isSyncingStatus;
    private string? _lastChatModelName;
    private RuntimeDeviceInfo _deviceInfo = RuntimeDeviceInfoProvider.Capture();

    [ObservableProperty] private bool _isEmbeddingBusy;
    [ObservableProperty] private bool _isEmbeddingEnabled;
    [ObservableProperty] private bool _isChatEnabled;
    [ObservableProperty] private EmbeddingSourceModeOption? _selectedEmbeddingSourceMode;
    [ObservableProperty] private EmbeddingBackendOption? _selectedLocalEmbeddingBackend;
    [ObservableProperty] private EmbeddingModelCandidateViewData? _selectedManagedEmbeddingModel;
    [ObservableProperty] private string _embeddingRemoteEndpoint = "";
    [ObservableProperty] private string _embeddingRemoteModelId = "";
    [ObservableProperty] private string _embeddingRemoteApiKey = "";
    [ObservableProperty] private int _embeddingContextSize = 8192;
    [ObservableProperty] private int _embeddingBatchSize = 8192;
    [ObservableProperty] private int _embeddingUBatchSize = 8192;
    [ObservableProperty] private int _embeddingGpuLayers;

    public ObservableCollection<EmbeddingSourceModeOption> EmbeddingSourceModeOptions { get; } = new();
    public ObservableCollection<EmbeddingBackendOption> LocalEmbeddingBackendOptions { get; } = new();
    public ObservableCollection<EmbeddingModelCandidateViewData> ManagedEmbeddingModels { get; } = new();

    public string ChatStatusKey => App.ModelService.IsLoading
        ? "Progress"
        : App.ModelService.CurIsRunning
            ? "Success"
            : "Neutral";

    public string ChatStatusText => App.ModelService.IsLoading
        ? Loc("ServicesStatusLoading")
        : App.ModelService.CurIsRunning
            ? Loc("ServicesStatusRunning")
            : Loc("ServicesStatusStopped");

    public string ChatCurrentModel =>
        App.ModelService.CurModelRunningData?.ModelName ?? Loc("ServicesNoModelRunning");

    public string ChatBackend => App.ModelService.CurModelRunningData?.IsRemoteModel == true
        ? Loc("ServicesRemoteApiService")
        : Loc("ServicesRuntimeBackendService");

    public string ChatEndpointOrPathLabel => App.ModelService.CurModelRunningData?.IsRemoteModel == true
        ? Loc("ServicesEndpoint")
        : Loc("ServicesModelPath");

    public string ChatModelPath => App.ModelService.CurModelRunningData?.ModelPath ?? "-";
    public string ChatRunningCount => string.Format(Loc("ServicesRunningCountFormat"), App.ModelService.CurRunningCount);
    public string ChatModelCount => string.Format(Loc("ServicesModelCountFormat"), App.ModelService.ModelSources.Count);
    public bool ChatIsLoading => App.ModelService.IsLoading;
    public bool ChatCanToggle => !App.ModelService.IsLoading;
    public bool ChatCanReload => !App.ModelService.IsLoading && !string.IsNullOrWhiteSpace(GetChatStartCandidateName());
    public double ChatLoadingPercent => Math.Clamp(App.ModelService.LoadingProgress, 0, 1) * 100;

    public string EmbeddingStatusKey => !string.IsNullOrWhiteSpace(_embeddingService.LastError)
        ? "Danger"
        : !IsEmbeddingReadyToStart
            ? "Warning"
            : _embeddingService.IsRunning
                ? "Success"
                : "Neutral";

    public string EmbeddingStatusText => !string.IsNullOrWhiteSpace(_embeddingService.LastError)
        ? Loc("ServicesStatusError")
        : !IsEmbeddingReadyToStart
            ? Loc("ServicesStatusNeedsConfig")
            : _embeddingService.IsRunning
                ? Loc("ServicesStatusRunning")
                : Loc("ServicesStatusStopped");

    public string EmbeddingRuntimeBackend => _embeddingService.BackendName;
    public string EmbeddingRuntimeModelPath => string.IsNullOrWhiteSpace(_embeddingService.ModelPath) ? "-" : _embeddingService.ModelPath;
    public string EmbeddingConfiguredModelPath => ResolveConfiguredEmbeddingPath();
    public string EmbeddingConfiguredModelDisplay => ResolveConfiguredEmbeddingDisplay();
    public string EmbeddingConfiguredSourceText => SelectedEmbeddingSourceMode?.DisplayName ?? "-";
    public string EmbeddingDimensionsText => _embeddingService.Dimensions > 0 ? _embeddingService.Dimensions.ToString() : "-";
    public string EmbeddingLastStartedText => _embeddingService.LastStartedAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "-";
    public string EmbeddingLastError => string.IsNullOrWhiteSpace(_embeddingService.LastError) ? Loc("ServicesNoError") : _embeddingService.LastError;
    public bool IsEmbeddingReadyToStart => SelectedEmbeddingSourceMode?.Mode switch
    {
        EmbeddingModelSettingConfig.SourceModeRemoteApi => !string.IsNullOrWhiteSpace(EmbeddingRemoteEndpoint) &&
                                                          !string.IsNullOrWhiteSpace(EmbeddingRemoteModelId),
        _ => SelectedManagedEmbeddingModel != null ||
             string.IsNullOrWhiteSpace(_embeddingConfig.ModelPath) && ManagedEmbeddingModels.Count > 0
    };

    public bool IsLocalEmbeddingSource =>
        SelectedEmbeddingSourceMode?.Mode != EmbeddingModelSettingConfig.SourceModeRemoteApi;

    public bool IsRemoteApiSource =>
        SelectedEmbeddingSourceMode?.Mode == EmbeddingModelSettingConfig.SourceModeRemoteApi;

    public string ManagedEmbeddingEmptyText => Loc("ServicesNoManagedEmbeddingModels");
    public bool HasManagedEmbeddingModels => ManagedEmbeddingModels.Count > 0;
    public bool NoManagedEmbeddingModels => !HasManagedEmbeddingModels;
    public string ManagedEmbeddingSummaryText => string.Format(
        Loc("ServicesManagedEmbeddingSummaryFormat"),
        ManagedEmbeddingModels.Count(x => x.Source == EmbeddingModelCandidateSource.Application),
        ManagedEmbeddingModels.Count(x => x.Source == EmbeddingModelCandidateSource.BuiltIn));

    public string RuntimeVersionName =>
        LlmManager.Instance.CurrentRuntimeVersion?.Name ?? Loc("ServicesNoRuntimeSelected");

    public string RuntimePath =>
        LlmManager.Instance.CurrentRuntimeVersion?.InstallDirectory ??
        SettingConfig.BackendRuntimeEnginePath;

    public string RemoteModelCount =>
        string.Format(Loc("ServicesRemoteModelCountFormat"), LlmManager.Instance.RemoteModelCount);

    public string FavoriteModel =>
        LlmManager.Instance.GetPreferredModelName(false) ?? "-";

    public string DeviceCpuUsageText => _deviceInfo.ProcessCpuUsagePercent <= 0
        ? "-"
        : $"{_deviceInfo.ProcessCpuUsagePercent:F1}%";

    public string DeviceMemoryText => $"{FormatBytes(_deviceInfo.AvailableMemoryBytes)} / {FormatBytes(_deviceInfo.TotalMemoryBytes)}";
    public string DeviceCpuName => string.IsNullOrWhiteSpace(_deviceInfo.CpuName) ? "-" : _deviceInfo.CpuName;
    public string DeviceGpuName => string.IsNullOrWhiteSpace(_deviceInfo.GpuName) ? "-" : _deviceInfo.GpuName;
    public string DeviceGpuMemoryText => _deviceInfo.HasGpuMemoryInfo
        ? $"{FormatBytes(_deviceInfo.GpuAvailableMemoryBytes)} / {FormatBytes(_deviceInfo.GpuTotalMemoryBytes)}"
        : "-";
    public string DeviceGpuMemoryNote => string.IsNullOrWhiteSpace(_deviceInfo.GpuMemoryNote) ? "-" : _deviceInfo.GpuMemoryNote;

    public ServicesPageData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ServicesPageData(IMessageService messageService)
    {
        _messageService = messageService;
        App.ModelService.PropertyChanged += (_, _) => RefreshStatus();
        _embeddingService.StateChanged += RefreshStatus;
        InitializeSourceModes();
        InitializeLocalEmbeddingBackends();
        LoadEmbeddingSettings();
        RefreshManagedEmbeddingModels();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        RefreshManagedEmbeddingModels();
        RefreshStatus();
    }

    protected override Control CreateView => new ServicesPage();

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        await App.ModelService.LoadModelList();
        RefreshManagedEmbeddingModels();
        RefreshStatus();
    }

    [RelayCommand]
    private void RefreshManagedEmbeddingModelList()
    {
        RefreshManagedEmbeddingModels();
        RefreshStatus();
    }

    [RelayCommand]
    private async Task ReloadChatModel()
    {
        string? modelName = await ResolveChatStartModelNameAsync();
        if (string.IsNullOrWhiteSpace(modelName))
        {
            _messageService.ShowNotification(Loc("ServicesChatModelNotSelected"), Loc("MessageInfoTitle"));
            GoToModelPage();
            return;
        }

        App.ModelService.EjectCurrentModel();
        await App.ModelService.LoadModelWithRiskConfirmationAsync(modelName);
        RefreshStatus();
    }

    [RelayCommand]
    private void SelectManagedEmbeddingModel(EmbeddingModelCandidateViewData? model)
    {
        if (model == null) return;
        SelectedManagedEmbeddingModel = model;
        SaveEmbeddingSettings(false);
    }

    [RelayCommand]
    private void OpenEmbeddingFolder()
    {
        App.FilesService.OpenFolder(EmbeddingModelSettingConfig.Current.ExternalEmbeddedModelPath);
    }

    [RelayCommand]
    private async Task SelectEmbeddingFolderAsync()
    {
        string path = await App.FilesService.OpenSelectFolderAsync(
            EmbeddingModelSettingConfig.Current.ExternalEmbeddedModelPath,
            UIManager.GetFocusWindow());
        if (string.IsNullOrWhiteSpace(path)) return;
        EmbeddingModelSettingConfig.Current.ExternalEmbeddedModelPath = path;
        EmbeddingModelSettingConfig.Current.Save();
        RefreshManagedEmbeddingModels();
        SaveEmbeddingSettings(false);
    }

    [RelayCommand]
    private void OpenRuntimeFolder()
    {
        App.FilesService.OpenFolder(RuntimePath);
    }

    [RelayCommand]
    private void GoToModelPage()
    {
        App.ViewModel.JumpToPage(MenuPages.MenuModelKey);
    }

    [RelayCommand]
    private void SaveEmbeddingSettings()
    {
        SaveEmbeddingSettings(true);
    }

    [RelayCommand]
    private async Task RestartEmbeddingAsync()
    {
        await RunEmbeddingActionAsync(() => _embeddingService.RestartAsync());
    }

    partial void OnSelectedEmbeddingSourceModeChanged(EmbeddingSourceModeOption? value)
    {
        if (value == null) return;
        OnPropertyChanged(nameof(IsLocalEmbeddingSource));
        OnPropertyChanged(nameof(IsRemoteApiSource));
        OnPropertyChanged(nameof(IsEmbeddingReadyToStart));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelPath));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelDisplay));
        OnPropertyChanged(nameof(EmbeddingConfiguredSourceText));
    }

    partial void OnSelectedLocalEmbeddingBackendChanged(EmbeddingBackendOption? value)
    {
        OnPropertyChanged(nameof(EmbeddingConfiguredModelPath));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelDisplay));
    }

    partial void OnSelectedManagedEmbeddingModelChanged(EmbeddingModelCandidateViewData? value)
    {
        OnPropertyChanged(nameof(IsEmbeddingReadyToStart));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelPath));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelDisplay));
    }

    partial void OnEmbeddingRemoteEndpointChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmbeddingReadyToStart));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelPath));
    }

    partial void OnEmbeddingRemoteModelIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmbeddingReadyToStart));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelDisplay));
    }

    partial void OnIsEmbeddingEnabledChanged(bool value)
    {
        if (_isSyncingStatus || IsEmbeddingBusy) return;
        if (value) _ = StartEmbeddingFromToggleAsync();
        else StopEmbeddingFromToggle();
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isSyncingStatus || App.ModelService.IsLoading) return;
        if (value) _ = StartChatFromToggleAsync();
        else StopChatFromToggle();
    }

    private async Task StartEmbeddingFromToggleAsync()
    {
        bool success = await RunEmbeddingActionAsync(() => _embeddingService.GetSessionAsync());
        if (!success)
        {
            _isSyncingStatus = true;
            IsEmbeddingEnabled = false;
            _isSyncingStatus = false;
        }
    }

    private void StopEmbeddingFromToggle()
    {
        _embeddingService.StopSession();
        RefreshStatus();
    }

    private async Task StartChatFromToggleAsync()
    {
        string? modelName = await ResolveChatStartModelNameAsync();
        if (string.IsNullOrWhiteSpace(modelName))
        {
            _messageService.ShowNotification(Loc("ServicesChatModelNotSelected"), Loc("MessageInfoTitle"));
            _isSyncingStatus = true;
            IsChatEnabled = false;
            _isSyncingStatus = false;
            GoToModelPage();
            return;
        }

        try
        {
            await App.ModelService.LoadModelWithRiskConfirmationAsync(modelName);
        }
        finally
        {
            RefreshStatus();
            if (!App.ModelService.CurIsRunning)
            {
                _isSyncingStatus = true;
                IsChatEnabled = false;
                _isSyncingStatus = false;
            }
        }
    }

    private void StopChatFromToggle()
    {
        _lastChatModelName = App.ModelService.CurModelRunningData?.ModelName ?? _lastChatModelName;
        App.ModelService.EjectCurrentModel();
        RefreshStatus();
    }

    private async Task<bool> RunEmbeddingActionAsync(Func<Task> action)
    {
        try
        {
            IsEmbeddingBusy = true;
            SaveEmbeddingSettings(false);
            await action();
            return true;
        }
        catch (Exception e)
        {
            _messageService.ShowNotification(e.Message, Loc("ServicesEmbeddingStartFailed"), MessageSeverity.Error);
            return false;
        }
        finally
        {
            IsEmbeddingBusy = false;
            RefreshStatus();
        }
    }

    private void InitializeSourceModes()
    {
        EmbeddingSourceModeOptions.Clear();
        EmbeddingSourceModeOptions.Add(new EmbeddingSourceModeOption(
            EmbeddingModelSettingConfig.SourceModeLocal,
            Loc("ServicesEmbeddingSourceLocal"),
            Loc("ServicesEmbeddingSourceLocalDesc")));
        EmbeddingSourceModeOptions.Add(new EmbeddingSourceModeOption(
            EmbeddingModelSettingConfig.SourceModeRemoteApi,
            Loc("ServicesEmbeddingSourceRemoteApi"),
            Loc("ServicesEmbeddingSourceRemoteApiDesc")));
    }

    private void InitializeLocalEmbeddingBackends()
    {
        LocalEmbeddingBackendOptions.Clear();
        LocalEmbeddingBackendOptions.Add(new EmbeddingBackendOption(
            EmbeddingModelSettingConfig.BackendLLamaSharp,
            "LLamaSharp"));
        LocalEmbeddingBackendOptions.Add(new EmbeddingBackendOption(
            EmbeddingModelSettingConfig.BackendLLamaCpp,
            "llama.cpp"));
    }

    private void LoadEmbeddingSettings()
    {
        string sourceMode = _embeddingConfig.SourceMode;
        if (string.IsNullOrWhiteSpace(sourceMode) ||
            sourceMode == EmbeddingModelSettingConfig.SourceModeManagedLocal ||
            sourceMode == EmbeddingModelSettingConfig.SourceModeCustomLocal)
            sourceMode = EmbeddingModelSettingConfig.SourceModeLocal;
        SelectedEmbeddingSourceMode = EmbeddingSourceModeOptions.FirstOrDefault(x => x.Mode == sourceMode) ??
                                      EmbeddingSourceModeOptions.First();
        string backend = _embeddingConfig.Backend;
        if (string.IsNullOrWhiteSpace(backend) ||
            string.Equals(backend, EmbeddingModelSettingConfig.BackendOpenAICompatible, StringComparison.OrdinalIgnoreCase))
            backend = EmbeddingModelSettingConfig.BackendLLamaSharp;
        SelectedLocalEmbeddingBackend = LocalEmbeddingBackendOptions.FirstOrDefault(x =>
            string.Equals(x.Backend, backend, StringComparison.OrdinalIgnoreCase)) ??
                                        LocalEmbeddingBackendOptions.First();
        EmbeddingRemoteEndpoint = _embeddingConfig.RemoteEndpoint;
        EmbeddingRemoteModelId = _embeddingConfig.RemoteModelId;
        EmbeddingRemoteApiKey = _embeddingConfig.RemoteApiKey;
        EmbeddingContextSize = _embeddingConfig.ContextSize;
        EmbeddingBatchSize = _embeddingConfig.BatchSize;
        EmbeddingUBatchSize = _embeddingConfig.UBatchSize;
        EmbeddingGpuLayers = _embeddingConfig.GpuLayers;
    }

    private void SaveEmbeddingSettings(bool notify)
    {
        string sourceMode = SelectedEmbeddingSourceMode?.Mode ?? EmbeddingModelSettingConfig.SourceModeLocal;
        _embeddingConfig.SourceMode = sourceMode;
        _embeddingConfig.Backend = sourceMode == EmbeddingModelSettingConfig.SourceModeRemoteApi
            ? EmbeddingModelSettingConfig.BackendOpenAICompatible
            : SelectedLocalEmbeddingBackend?.Backend ?? EmbeddingModelSettingConfig.BackendLLamaSharp;
        _embeddingConfig.ModelPath = sourceMode == EmbeddingModelSettingConfig.SourceModeLocal
            ? SelectedManagedEmbeddingModel?.Path ?? _embeddingConfig.ModelPath
            : "";
        _embeddingConfig.RemoteEndpoint = EmbeddingRemoteEndpoint;
        _embeddingConfig.RemoteModelId = EmbeddingRemoteModelId;
        _embeddingConfig.RemoteApiKey = EmbeddingRemoteApiKey;
        _embeddingConfig.ContextSize = Math.Max(0, EmbeddingContextSize);
        _embeddingConfig.BatchSize = Math.Max(0, EmbeddingBatchSize);
        _embeddingConfig.UBatchSize = Math.Max(0, EmbeddingUBatchSize);
        _embeddingConfig.GpuLayers = EmbeddingGpuLayers;
        _embeddingConfig.Save();
        if (notify) _messageService.ShowNotification(Loc("ServicesEmbeddingSettingsSaved"));
        RefreshStatus();
    }

    private void RefreshManagedEmbeddingModels()
    {
        string selectedPath = _embeddingConfig.ModelPath;
        ManagedEmbeddingModels.Clear();
        foreach (EmbeddingModelCandidate candidate in EmbeddingModelService.GetManagedCandidates())
        {
            ManagedEmbeddingModels.Add(new EmbeddingModelCandidateViewData(
                candidate,
                candidate.Source == EmbeddingModelCandidateSource.Application
                    ? Loc("ServicesEmbeddingSourceApplication")
                    : Loc("ServicesEmbeddingSourceBuiltIn")));
        }

        SelectedManagedEmbeddingModel = ManagedEmbeddingModels.FirstOrDefault(x =>
            string.Equals(x.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (SelectedManagedEmbeddingModel == null && string.IsNullOrWhiteSpace(selectedPath))
            SelectedManagedEmbeddingModel = ManagedEmbeddingModels.FirstOrDefault();
        OnPropertyChanged(nameof(HasManagedEmbeddingModels));
        OnPropertyChanged(nameof(NoManagedEmbeddingModels));
        OnPropertyChanged(nameof(ManagedEmbeddingSummaryText));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelDisplay));
    }

    private string ResolveConfiguredEmbeddingPath()
    {
        return SelectedEmbeddingSourceMode?.Mode switch
        {
            EmbeddingModelSettingConfig.SourceModeRemoteApi => string.IsNullOrWhiteSpace(EmbeddingRemoteEndpoint)
                ? "-"
                : EmbeddingRemoteEndpoint,
            _ => SelectedManagedEmbeddingModel?.Path ??
                 (string.IsNullOrWhiteSpace(_embeddingConfig.ModelPath) ? "-" : _embeddingConfig.ModelPath)
        };
    }

    private string ResolveConfiguredEmbeddingDisplay()
    {
        return SelectedEmbeddingSourceMode?.Mode switch
        {
            EmbeddingModelSettingConfig.SourceModeRemoteApi => string.IsNullOrWhiteSpace(EmbeddingRemoteModelId)
                ? "-"
                : EmbeddingRemoteModelId,
            _ => SelectedManagedEmbeddingModel?.Name ??
                 (string.IsNullOrWhiteSpace(_embeddingConfig.ModelPath) ? "-" : Path.GetFileName(_embeddingConfig.ModelPath))
        };
    }

    private async Task<string?> ResolveChatStartModelNameAsync()
    {
        string? candidateName = GetChatStartCandidateName();
        if (!string.IsNullOrWhiteSpace(candidateName)) return candidateName;

        await App.ModelService.LoadModelList();
        return GetChatStartCandidateName();
    }

    private string? GetChatStartCandidateName()
    {
        return App.ModelService.CurModelRunningData?.ModelName ??
               _lastChatModelName ??
               LlmManager.Instance.GetPreferredModelName(false);
    }

    private void RefreshStatus()
    {
        _deviceInfo = RuntimeDeviceInfoProvider.Capture();
        if (App.ModelService.CurModelRunningData != null)
            _lastChatModelName = App.ModelService.CurModelRunningData.ModelName;

        _isSyncingStatus = true;
        IsEmbeddingEnabled = _embeddingService.IsRunning;
        IsChatEnabled = App.ModelService.CurIsRunning;
        _isSyncingStatus = false;
        OnPropertyChanged(nameof(ChatStatusKey));
        OnPropertyChanged(nameof(ChatStatusText));
        OnPropertyChanged(nameof(ChatCurrentModel));
        OnPropertyChanged(nameof(ChatBackend));
        OnPropertyChanged(nameof(ChatEndpointOrPathLabel));
        OnPropertyChanged(nameof(ChatModelPath));
        OnPropertyChanged(nameof(ChatRunningCount));
        OnPropertyChanged(nameof(ChatModelCount));
        OnPropertyChanged(nameof(ChatIsLoading));
        OnPropertyChanged(nameof(ChatCanToggle));
        OnPropertyChanged(nameof(ChatCanReload));
        OnPropertyChanged(nameof(ChatLoadingPercent));
        OnPropertyChanged(nameof(EmbeddingStatusKey));
        OnPropertyChanged(nameof(EmbeddingStatusText));
        OnPropertyChanged(nameof(EmbeddingRuntimeBackend));
        OnPropertyChanged(nameof(EmbeddingRuntimeModelPath));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelPath));
        OnPropertyChanged(nameof(EmbeddingConfiguredModelDisplay));
        OnPropertyChanged(nameof(EmbeddingConfiguredSourceText));
        OnPropertyChanged(nameof(ManagedEmbeddingSummaryText));
        OnPropertyChanged(nameof(EmbeddingDimensionsText));
        OnPropertyChanged(nameof(EmbeddingLastStartedText));
        OnPropertyChanged(nameof(EmbeddingLastError));
        OnPropertyChanged(nameof(IsEmbeddingReadyToStart));
        OnPropertyChanged(nameof(RuntimeVersionName));
        OnPropertyChanged(nameof(RuntimePath));
        OnPropertyChanged(nameof(RemoteModelCount));
        OnPropertyChanged(nameof(FavoriteModel));
        OnPropertyChanged(nameof(DeviceCpuUsageText));
        OnPropertyChanged(nameof(DeviceMemoryText));
        OnPropertyChanged(nameof(DeviceCpuName));
        OnPropertyChanged(nameof(DeviceGpuName));
        OnPropertyChanged(nameof(DeviceGpuMemoryText));
        OnPropertyChanged(nameof(DeviceGpuMemoryNote));
    }

    private static string Loc(string key)
    {
        return LocalizationManager.Instance.GetString(key);
    }

    private static string FormatBytes(long bytes)
    {
        return bytes <= 0 ? "-" : GameUtils.FormatBytes(bytes);
    }
}

public sealed record EmbeddingSourceModeOption(string Mode, string DisplayName, string Description);

public sealed record EmbeddingBackendOption(string Backend, string DisplayName);

public sealed class EmbeddingModelCandidateViewData
{
    public EmbeddingModelCandidateViewData(EmbeddingModelCandidate candidate, string sourceText)
    {
        Name = candidate.Name;
        Path = candidate.Path;
        Source = candidate.Source;
        SourceText = sourceText;
        SizeText = GameUtils.FormatBytes(candidate.SizeBytes);
    }

    public string Name { get; }
    public string Path { get; }
    public EmbeddingModelCandidateSource Source { get; }
    public string SourceText { get; }
    public string SizeText { get; }
}
