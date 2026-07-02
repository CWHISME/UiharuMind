/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.AI.Models;
using UiharuMind.Services;

namespace UiharuMind.ViewModels.ViewData;

public partial class ModelRuntimeBasicSettingsData : ObservableObject
{
    private readonly ModelRuntimeSettingConfig _config = ModelRuntimeSettingConfig.Current;

    [ObservableProperty] private string _engineType;
    [ObservableProperty] private string _llamaSharpBackendMode;
    [ObservableProperty] private int _contextSize;
    [ObservableProperty] private int _gpuLayers;
    [ObservableProperty] private int _batchSize;
    [ObservableProperty] private int _uBatchSize;
    [ObservableProperty] private int _threads;
    [ObservableProperty] private bool _flashAttention;
    [ObservableProperty] private bool _showAdvancedSettings;

    public int MaxContextSize => Math.Max(4096, GetCurrentLocalModelInfo()?.ContextLength ?? 131072);
    public int MaxGpuLayers => Math.Max(128, GetCurrentLocalModelInfo()?.LayerCount ?? 128);

    public string[] EngineOptions { get; } =
    {
        ModelRuntimeSettingConfig.EngineLLamaSharp,
        ModelRuntimeSettingConfig.EngineLLamaCpp
    };

    public string[] LLamaSharpBackendOptions { get; } =
    {
        ModelRuntimeSettingConfig.LLamaSharpBackendAuto,
        ModelRuntimeSettingConfig.LLamaSharpBackendCpu,
        ModelRuntimeSettingConfig.LLamaSharpBackendGpu
    };

    public bool IsLLamaSharpEngine => EngineType == ModelRuntimeSettingConfig.EngineLLamaSharp;
    public bool IsLLamaCppEngine => EngineType == ModelRuntimeSettingConfig.EngineLLamaCpp;
    public string LLamaSharpBackendModeLabel => string.IsNullOrWhiteSpace(LlamaSharpBackendMode)
        ? ModelRuntimeSettingConfig.LLamaSharpBackendAuto
        : LlamaSharpBackendMode;
    public string CurrentModelName => GetCurrentModel()?.ModelName ?? "-";
    public string CurrentModelDetailText => GetCurrentModelDetailText();
    public string EstimatedGpuMemoryText => GetRiskEstimateText();
    public string EstimatedTotalMemoryText => GetRiskEstimateText();
    public string AvailableMemoryText => GetAvailableMemoryText();
    public string RuntimeRiskText => FormatRiskLevel(GetCurrentLoadRisk().Level);
    public string RuntimeRiskDetailText => BuildRiskDetail(GetCurrentLoadRisk());
    public string ResolvedParametersText => GetResolvedParametersText();

    public ModelRuntimeBasicSettingsData()
    {
        _engineType = NormalizeEngineType(_config.EngineType);
        _llamaSharpBackendMode = NormalizeBackendMode(_config.LLamaSharpBackendMode);
        _config.EngineType = _engineType;
        _config.LLamaSharpBackendMode = _llamaSharpBackendMode;
        _contextSize = _config.ContextSize;
        _gpuLayers = _config.GpuLayers;
        _batchSize = _config.BatchSize;
        _uBatchSize = _config.UBatchSize;
        _threads = _config.Threads;
        _flashAttention = _config.FlashAttention;

        if (App.ModelService != null)
            App.ModelService.PropertyChanged += OnModelServicePropertyChanged;
    }

    private void OnModelServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(App.ModelService.CurModelRunningData)) return;
        OnPropertyChanged(nameof(CurrentModelName));
        OnPropertyChanged(nameof(CurrentModelDetailText));
        OnPropertyChanged(nameof(EstimatedGpuMemoryText));
        OnPropertyChanged(nameof(EstimatedTotalMemoryText));
        OnPropertyChanged(nameof(AvailableMemoryText));
        OnPropertyChanged(nameof(RuntimeRiskText));
        OnPropertyChanged(nameof(RuntimeRiskDetailText));
        OnPropertyChanged(nameof(ResolvedParametersText));
        OnPropertyChanged(nameof(MaxContextSize));
        OnPropertyChanged(nameof(MaxGpuLayers));
    }

    partial void OnEngineTypeChanged(string value)
    {
        string normalized = NormalizeEngineType(value);
        if (normalized != value)
        {
            _engineType = normalized;
            OnPropertyChanged(nameof(EngineType));
        }

        _config.EngineType = normalized;
        Save();
        OnPropertyChanged(nameof(IsLLamaSharpEngine));
        OnPropertyChanged(nameof(IsLLamaCppEngine));
        RefreshComputedProperties();
    }

    partial void OnLlamaSharpBackendModeChanged(string value)
    {
        string normalized = NormalizeBackendMode(value);
        if (normalized != value)
        {
            _llamaSharpBackendMode = normalized;
            OnPropertyChanged(nameof(LlamaSharpBackendMode));
        }

        _config.LLamaSharpBackendMode = normalized;
        Save();
        OnPropertyChanged(nameof(LLamaSharpBackendModeLabel));
        RefreshComputedProperties();
    }

    partial void OnContextSizeChanged(int value)
    {
        _config.ContextSize = Math.Max(0, value);
        Save();
        RefreshComputedProperties();
    }

    partial void OnGpuLayersChanged(int value)
    {
        _config.GpuLayers = value;
        Save();
        RefreshComputedProperties();
    }

    partial void OnBatchSizeChanged(int value)
    {
        _config.BatchSize = Math.Max(0, value);
        Save();
        RefreshComputedProperties();
    }

    partial void OnUBatchSizeChanged(int value)
    {
        _config.UBatchSize = Math.Max(0, value);
        Save();
        RefreshComputedProperties();
    }

    partial void OnThreadsChanged(int value)
    {
        _config.Threads = Math.Max(0, value);
        Save();
        RefreshComputedProperties();
    }

    partial void OnFlashAttentionChanged(bool value)
    {
        _config.FlashAttention = value;
        Save();
        RefreshComputedProperties();
    }

    private static void Save()
    {
        ModelRuntimeSettingConfig.Current.Save();
    }

    private static string NormalizeEngineType(string? value)
    {
        return value is ModelRuntimeSettingConfig.EngineLLamaCpp or ModelRuntimeSettingConfig.EngineLLamaSharp
            ? value
            : ModelRuntimeSettingConfig.EngineLLamaSharp;
    }

    private static string NormalizeBackendMode(string? value)
    {
        return value is ModelRuntimeSettingConfig.LLamaSharpBackendCpu
            or ModelRuntimeSettingConfig.LLamaSharpBackendGpu
            or ModelRuntimeSettingConfig.LLamaSharpBackendAuto
            ? value
            : ModelRuntimeSettingConfig.LLamaSharpBackendAuto;
    }

    private static ModelRunningData? GetCurrentModel()
    {
        try
        {
            return App.ModelService?.CurModelRunningData;
        }
        catch
        {
            return null;
        }
    }

    private static GGufModelInfo? GetCurrentLocalModelInfo()
    {
        return GetCurrentModel()?.ModelInfo as GGufModelInfo;
    }

    private static string GetCurrentModelDetailText()
    {
        ModelRunningData? model = GetCurrentModel();
        if (model == null) return "-";
        return model.IsRemoteModel ? model.ModelPath : Path.GetFileName(model.ModelPath);
    }

    private void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(EstimatedGpuMemoryText));
        OnPropertyChanged(nameof(EstimatedTotalMemoryText));
        OnPropertyChanged(nameof(AvailableMemoryText));
        OnPropertyChanged(nameof(RuntimeRiskText));
        OnPropertyChanged(nameof(RuntimeRiskDetailText));
        OnPropertyChanged(nameof(ResolvedParametersText));
    }

    private static string GetRiskEstimateText()
    {
        RuntimeLoadRisk risk = GetCurrentLoadRisk();
        return risk.EstimatedTotalBytes > 0 ? GameUtils.FormatBytes(risk.EstimatedTotalBytes) : GetEstimatedModelSizeText();
    }

    private static string GetAvailableMemoryText()
    {
        RuntimeDeviceInfo info = RuntimeDeviceInfoProvider.Capture();
        return info.AvailableMemoryBytes > 0 ? GameUtils.FormatBytes(info.AvailableMemoryBytes) : "-";
    }

    private static string GetEstimatedModelSizeText()
    {
        ModelRunningData? model = GetCurrentModel();
        if (model?.IsRemoteModel != false || !File.Exists(model.ModelPath)) return "-";
        return GameUtils.FormatBytes(new FileInfo(model.ModelPath).Length);
    }

    private static RuntimeLoadRisk GetCurrentLoadRisk()
    {
        ModelRunningData? model = GetCurrentModel();
        return model?.IsRemoteModel == false
            ? App.ModelService is null ? RuntimeLoadRisk.Low : UiharuMind.Core.AI.LlmManager.Instance.AnalyzeLoadRisk(model.ModelName)
            : RuntimeLoadRisk.Low;
    }

    private static string FormatRiskLevel(RuntimeLoadRiskLevel level)
    {
        return level switch
        {
            RuntimeLoadRiskLevel.Danger => L("ModelRuntimeRiskDanger"),
            RuntimeLoadRiskLevel.Warning => L("ModelRuntimeRiskWarning"),
            RuntimeLoadRiskLevel.Unknown => L("ModelRuntimeRiskUnknown"),
            _ => L("ModelRuntimeRiskLow")
        };
    }

    private static string BuildRiskDetail(RuntimeLoadRisk risk)
    {
        string detail = string.Format(
            L("ModelRuntimeRiskDetailFormat"),
            risk.EstimatedTotalBytes > 0 ? GameUtils.FormatBytes(risk.EstimatedTotalBytes) : "-",
            risk.EstimatedKvCacheBytes > 0 ? GameUtils.FormatBytes(risk.EstimatedKvCacheBytes) : "-",
            string.IsNullOrWhiteSpace(risk.Reason) ? "-" : risk.Reason);
        if (risk.Warnings.Count == 0) return detail;
        return detail + Environment.NewLine + string.Join(Environment.NewLine, risk.Warnings.Select(x => $"- {x}"));
    }

    private static string GetResolvedParametersText()
    {
        RuntimeLoadRisk risk = GetCurrentLoadRisk();
        return risk.Level == RuntimeLoadRiskLevel.Low && risk.EstimatedTotalBytes <= 0
            ? "-"
            : risk.Reason;
    }

    private static string L(string key)
    {
        return LocalizationManager.Instance.GetString(key);
    }
}
