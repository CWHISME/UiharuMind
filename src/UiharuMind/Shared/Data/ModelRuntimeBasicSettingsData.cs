/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using UiharuMind.Core.AI;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Features.Models;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Shared.Data;

public partial class ModelRuntimeBasicSettingsData : ObservableObject
{
    private readonly ModelRuntimeSettingConfig _config = ModelRuntimeSettingConfig.Current;
    private readonly SettingsWriteBack _writeBack = new(() => ModelRuntimeSettingConfig.Current.Save()); //写回闸门

    [ObservableProperty] private LocalEngineOption? _selectedLocalEngine;
    [ObservableProperty] private int _contextSize;
    [ObservableProperty] private int _gpuLayers;
    [ObservableProperty] private SettingChoice<EGpuLayerMode> _selectedGpuLayerMode;
    [ObservableProperty] private SettingChoice<bool?> _selectedFlashAttention;
    private int _lastCustomGpuLayers; //从自动切回指定层数时沿用上次的数
    [ObservableProperty] private int _batchSize;
    [ObservableProperty] private int _uBatchSize;
    [ObservableProperty] private int _threads;
    [ObservableProperty] private bool _showAdvancedSettings;

    /// <summary>
    /// 已注册的本地引擎
    /// </summary>
    public IReadOnlyList<LocalEngineOption> LocalEngineOptions { get; }

    /// <summary>
    /// GPU 层数的三种取法
    /// </summary>
    public IReadOnlyList<SettingChoice<EGpuLayerMode>> GpuLayerModeOptions { get; } =
    [
        new(EGpuLayerMode.Auto, Loc.Text(LangKey.ModelRuntimeGpuLayersAuto)),
        new(EGpuLayerMode.CpuOnly, Loc.Text(LangKey.ModelRuntimeGpuLayersCpu)),
        new(EGpuLayerMode.Custom, Loc.Text(LangKey.ModelRuntimeGpuLayersCustom))
    ];

    /// <summary>
    /// Flash Attention 可选项
    /// </summary>
    public IReadOnlyList<SettingChoice<bool?>> FlashAttentionOptions { get; } =
    [
        new(null, Loc.Text(LangKey.ModelRuntimeOptionAuto)),
        new(true, Loc.Text(LangKey.ModelRuntimeOptionOn)),
        new(false, Loc.Text(LangKey.ModelRuntimeOptionOff))
    ];

    /// <summary>
    /// 选了「指定层数」才显示层数输入
    /// </summary>
    public bool IsCustomGpuLayers => SelectedGpuLayerMode.Value == EGpuLayerMode.Custom;

    /// <summary>
    /// 本地引擎多于一个才需要选
    /// </summary>
    public bool HasEngineChoice => LocalEngineOptions.Count > 1;

    public int MaxContextSize => Math.Max(4096, GetCurrentLocalModelInfo()?.ContextLength ?? 131072);
    public int MaxGpuLayers => Math.Max(128, GetCurrentLocalModelInfo()?.LayerCount ?? 128);

    public string CurrentModelName => GetCurrentModel()?.ModelName ?? "-";
    public string CurrentModelDetailText => GetCurrentModelDetailText();
    public string EstimatedGpuMemoryText => GetRiskEstimateText();
    public string EstimatedTotalMemoryText => GetRiskEstimateText();
    public string AvailableMemoryText => GetAvailableMemoryText();
    public string RuntimeRiskText => RuntimeRiskLabels.Format(GetCurrentLoadRisk().Level);
    public string RuntimeRiskDetailText => BuildRiskDetail(GetCurrentLoadRisk());
    public string ResolvedParametersText => GetResolvedParametersText();

    public ModelRuntimeBasicSettingsData()
    {
        // 回填走 backing field,不惊动生成的 OnXChanged——那六个 handler 每个都会跑一遍
        // RefreshComputedProperties(),而它背后是显存占用估算,没必要在构造时算六遍
        LocalEngineOptions = LlmManager.Instance.LocalEngines
            .Select(x => new LocalEngineOption(x.Id, x.DisplayName))
            .ToList();
        using (_writeBack.BeginLoad())
        {
            _selectedLocalEngine = LocalEngineOptions.FirstOrDefault(x => x.Id == _config.LocalEngineId)
                                   ?? LocalEngineOptions.FirstOrDefault();
            _contextSize = _config.ContextSize;
            _gpuLayers = _config.GpuLayers;
            _lastCustomGpuLayers = _config.GpuLayers > 0 ? _config.GpuLayers : 0;
            _selectedGpuLayerMode = GpuLayerModeOptions[(int)ModeOf(_config.GpuLayers)];
            _selectedFlashAttention = FlashAttentionOptions.First(x => x.Value == _config.FlashAttention);
            _batchSize = _config.BatchSize;
            _uBatchSize = _config.UBatchSize;
            _threads = _config.Threads;
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

    partial void OnSelectedLocalEngineChanged(LocalEngineOption? value)
    {
        _config.LocalEngineId = value?.Id ?? "";
        _writeBack.Save();
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
        if (value > 0) _lastCustomGpuLayers = value;
        if (!_writeBack.IsLoading) SelectedGpuLayerMode = GpuLayerModeOptions[(int)ModeOf(value)];
        _writeBack.Save();
        RefreshComputedProperties();
    }

    partial void OnSelectedGpuLayerModeChanged(SettingChoice<EGpuLayerMode> value)
    {
        OnPropertyChanged(nameof(IsCustomGpuLayers));
        if (ModeOf(GpuLayers) == value.Value) return;
        GpuLayers = value.Value switch
        {
            EGpuLayerMode.Auto => -1,
            EGpuLayerMode.CpuOnly => 0,
            _ => _lastCustomGpuLayers > 0 ? _lastCustomGpuLayers : MaxGpuLayers
        };
    }

    partial void OnSelectedFlashAttentionChanged(SettingChoice<bool?> value)
    {
        _config.FlashAttention = value.Value;
        _writeBack.Save();
        RefreshComputedProperties();
    }

    /// <summary>
    /// 恢复上下文、GPU 层数、批处理、线程与 Flash Attention 的默认值
    /// </summary>
    public void ResetToDefaults()
    {
        ContextSize = 0;
        GpuLayers = -1;
        BatchSize = 0;
        UBatchSize = 0;
        Threads = 0;
        SelectedFlashAttention = FlashAttentionOptions[0];
    }

    private static EGpuLayerMode ModeOf(int gpuLayers) => gpuLayers switch
    {
        < 0 => EGpuLayerMode.Auto,
        0 => EGpuLayerMode.CpuOnly,
        _ => EGpuLayerMode.Custom
    };

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


    private static string BuildRiskDetail(RuntimeLoadRisk risk)
    {
        string detail = string.Format(
            Loc.Text(LangKey.ModelRuntimeRiskDetailFormat),
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

/// <summary>
/// GPU 层数的取法：自动交给 llama-server 按显存放，纯 CPU 为 0，指定为具体层数
/// </summary>
public enum EGpuLayerMode
{
    Auto,
    CpuOnly,
    Custom
}

/// <summary>
/// 本地引擎选项
/// </summary>
/// <param name="Id">引擎 Id</param>
/// <param name="DisplayName">显示名</param>
public sealed record LocalEngineOption(string Id, string DisplayName);
