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
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Features.Models;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Shared.Controls;

public partial class ModelRuntimeBasicSettingsData : ObservableObject
{
    private readonly ModelRuntimeSettingConfig _config = ModelRuntimeSettingConfig.Current;
    private readonly SettingsWriteBack _writeBack = new(() => ModelRuntimeSettingConfig.Current.Save()); //写回闸门

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
        // 回填走 backing field,不惊动生成的 OnXChanged——那七个 handler 每个都会跑一遍
        // RefreshComputedProperties(),而它背后是显存占用估算,没必要在构造时算七遍
        using (_writeBack.BeginLoad())
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
        }

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
            EngineType = normalized;
        }

        _config.EngineType = normalized;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsLLamaSharpEngine));
        OnPropertyChanged(nameof(IsLLamaCppEngine));
        RefreshComputedProperties();
    }

    partial void OnLlamaSharpBackendModeChanged(string value)
    {
        string normalized = NormalizeBackendMode(value);
        if (normalized != value)
        {
            LlamaSharpBackendMode = normalized;
        }

        _config.LLamaSharpBackendMode = normalized;
        _writeBack.Save();
        OnPropertyChanged(nameof(LLamaSharpBackendModeLabel));
        RefreshComputedProperties();
    }

    partial void OnContextSizeChanged(int value)
    {
        _config.ContextSize = Math.Max(0, value);
        _writeBack.Save();
        RefreshComputedProperties();
    }

    partial void OnGpuLayersChanged(int value)
    {
        _config.GpuLayers = value;
        _writeBack.Save();
        RefreshComputedProperties();
    }

    partial void OnBatchSizeChanged(int value)
    {
        _config.BatchSize = Math.Max(0, value);
        _writeBack.Save();
        RefreshComputedProperties();
    }

    partial void OnUBatchSizeChanged(int value)
    {
        _config.UBatchSize = Math.Max(0, value);
        _writeBack.Save();
        RefreshComputedProperties();
    }

    partial void OnThreadsChanged(int value)
    {
        _config.Threads = Math.Max(0, value);
        _writeBack.Save();
        RefreshComputedProperties();
    }

    partial void OnFlashAttentionChanged(bool value)
    {
        _config.FlashAttention = value;
        _writeBack.Save();
        RefreshComputedProperties();
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
            RuntimeLoadRiskLevel.Danger => Loc.Text("ModelRuntimeRiskDanger"),
            RuntimeLoadRiskLevel.Warning => Loc.Text("ModelRuntimeRiskWarning"),
            RuntimeLoadRiskLevel.Unknown => Loc.Text("ModelRuntimeRiskUnknown"),
            _ => Loc.Text("ModelRuntimeRiskLow")
        };
    }

    private static string BuildRiskDetail(RuntimeLoadRisk risk)
    {
        string detail = string.Format(
            Loc.Text("ModelRuntimeRiskDetailFormat"),
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
}
