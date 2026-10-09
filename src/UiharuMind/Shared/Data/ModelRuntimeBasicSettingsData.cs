/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Data;

/// <summary>
/// 全局运行参数（上下文、GPU 层数、批大小、线程、Flash Attention），设置页「本地模型（文本）」用；
/// 某个模型自己的参数归 <c>ModelRuntimeOverridesViewData</c>
/// </summary>
public partial class ModelRuntimeBasicSettingsData : ObservableObject
{
    private readonly ModelRuntimeSettingConfig _config = ModelRuntimeSettingConfig.Current;
    private readonly SettingsWriteBack _writeBack = new(() => ModelRuntimeSettingConfig.Current.Save()); //写回闸门
    private int _lastCustomGpuLayers; //从自动切回指定层数时沿用上次的数

    [ObservableProperty] private int _contextSize;
    [ObservableProperty] private int _gpuLayers;
    [ObservableProperty] private SettingChoice<EGpuLayerMode> _selectedGpuLayerMode;
    [ObservableProperty] private SettingChoice<bool?> _selectedFlashAttention;
    [ObservableProperty] private int _batchSize;
    [ObservableProperty] private int _uBatchSize;
    [ObservableProperty] private int _threads;

    public ModelRuntimeBasicSettingsData()
    {
        // 回填走 backing field，不惊动生成的 OnXChanged（每个都会落一次盘）
        using (_writeBack.BeginLoad())
        {
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
    /// 层数输入上限：当前模型的层数，至少 128
    /// </summary>
    public int MaxGpuLayers =>
        Math.Max(128, (App.ModelService?.CurModelRunningData?.ModelInfo as GGufModelInfo)?.LayerCount ?? 128);

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

    private void OnModelServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(App.ModelService.CurModelRunningData)) OnPropertyChanged(nameof(MaxGpuLayers));
    }

    partial void OnContextSizeChanged(int value)
    {
        _config.ContextSize = Math.Max(0, value);
        _writeBack.Save();
    }

    partial void OnGpuLayersChanged(int value)
    {
        _config.GpuLayers = value;
        if (value > 0) _lastCustomGpuLayers = value;
        if (!_writeBack.IsLoading) SelectedGpuLayerMode = GpuLayerModeOptions[(int)ModeOf(value)];
        _writeBack.Save();
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
    }

    partial void OnBatchSizeChanged(int value)
    {
        _config.BatchSize = Math.Max(0, value);
        _writeBack.Save();
    }

    partial void OnUBatchSizeChanged(int value)
    {
        _config.UBatchSize = Math.Max(0, value);
        _writeBack.Save();
    }

    partial void OnThreadsChanged(int value)
    {
        _config.Threads = Math.Max(0, value);
        _writeBack.Save();
    }

    private static EGpuLayerMode ModeOf(int gpuLayers) => gpuLayers switch
    {
        < 0 => EGpuLayerMode.Auto,
        0 => EGpuLayerMode.CpuOnly,
        _ => EGpuLayerMode.Custom
    };
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
