using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Data;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models;

/// <summary>
/// 可单独给某个模型设置的一项：没改过时显示全局值，改了就记成这个模型自己的，恢复后回到跟随全局
/// </summary>
public partial class OverridableIntSetting : ObservableObject
{
    private readonly Func<int> _globalValue;
    private readonly Func<int, string> _format;
    private bool _syncing; //回填期间不算用户改动

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isOverridden;

    [ObservableProperty] private int _value;

    /// <param name="globalValue">全局值</param>
    /// <param name="overrideValue">这个模型自己的值，没有为 null</param>
    /// <param name="format">取值的显示（如 0 显示「自动」）</param>
    public OverridableIntSetting(Func<int> globalValue, int? overrideValue, Func<int, string> format)
    {
        _globalValue = globalValue;
        _format = format;
        _isOverridden = overrideValue != null;
        _value = overrideValue ?? globalValue();
    }

    /// <summary>
    /// 用户改了值或恢复了跟随全局
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 行说明：跟随全局时报全局值，单独设置时报全局是多少
    /// </summary>
    public string StatusText => Loc.Text(IsOverridden ? LangKey.ModelParamsOverridden : LangKey.ModelParamsFollowGlobal,
        _format(_globalValue()));

    /// <summary>
    /// 写进覆写的值，跟随全局为 null
    /// </summary>
    public int? OverrideValue => IsOverridden ? Value : null;

    partial void OnValueChanged(int value)
    {
        if (_syncing) return;
        IsOverridden = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// 改回跟随全局
    /// </summary>
    [RelayCommand]
    public void Reset()
    {
        if (!IsOverridden) return;
        _syncing = true;
        try
        {
            IsOverridden = false;
            Value = _globalValue();
        }
        finally
        {
            _syncing = false;
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// 某个本地模型自己的运行参数（上下文、GPU 层数、批大小、线程），改动随手落盘，下次加载生效
/// </summary>
public partial class ModelRuntimeOverridesViewData : ObservableObject
{
    private readonly ModelRuntimeSettingConfig _config;
    private readonly Action _save;
    private readonly Func<RuntimeLoadRisk> _analyzeRisk;
    private bool _syncingGpuMode; //层数与取法互相回填时防重入
    private int _lastCustomGpuLayers;

    [ObservableProperty] private SettingChoice<EGpuLayerMode> _selectedGpuLayerMode;

    /// <param name="modelName">模型名</param>
    /// <param name="config">全局运行设置（覆写也存在这里）</param>
    /// <param name="save">落盘</param>
    /// <param name="analyzeRisk">按当前生效参数估算加载风险</param>
    /// <param name="isRunning">模型正在运行</param>
    /// <param name="maxGpuLayers">模型层数，切到指定层数时的初值</param>
    public ModelRuntimeOverridesViewData(string modelName, ModelRuntimeSettingConfig config, Action save,
        Func<RuntimeLoadRisk> analyzeRisk, bool isRunning, int maxGpuLayers)
    {
        ModelName = modelName;
        _config = config;
        _save = save;
        _analyzeRisk = analyzeRisk;
        IsRunning = isRunning;
        MaxGpuLayers = Math.Max(1, maxGpuLayers);

        ModelRuntimeOverrides? overrides = config.GetOverrides(modelName);
        ContextSize = new OverridableIntSetting(() => _config.ContextSize, overrides?.ContextSize, FormatAuto);
        GpuLayers = new OverridableIntSetting(() => _config.GpuLayers, overrides?.GpuLayers, FormatGpuLayers);
        BatchSize = new OverridableIntSetting(() => _config.BatchSize, overrides?.BatchSize, FormatAuto);
        UBatchSize = new OverridableIntSetting(() => _config.UBatchSize, overrides?.UBatchSize, FormatAuto);
        Threads = new OverridableIntSetting(() => _config.Threads, overrides?.Threads, FormatAuto);

        _lastCustomGpuLayers = GpuLayers.Value > 0 ? GpuLayers.Value : MaxGpuLayers;
        _selectedGpuLayerMode = GpuLayerModeOptions[(int)ModeOf(GpuLayers.Value)];
        foreach (OverridableIntSetting setting in new[] { ContextSize, GpuLayers, BatchSize, UBatchSize, Threads })
            setting.Changed += OnSettingChanged;
        GpuLayers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OverridableIntSetting.Value)) return;
            if (GpuLayers.Value > 0) _lastCustomGpuLayers = GpuLayers.Value;
            _syncingGpuMode = true;
            SelectedGpuLayerMode = GpuLayerModeOptions[(int)ModeOf(GpuLayers.Value)];
            _syncingGpuMode = false;
        };
    }

    /// <summary>
    /// 模型名
    /// </summary>
    public string ModelName { get; }

    /// <summary>
    /// 模型正在运行：改动要重新加载才生效
    /// </summary>
    public bool IsRunning { get; }

    /// <summary>
    /// 层数输入上限
    /// </summary>
    public int MaxGpuLayers { get; }

    /// <summary>
    /// 上下文长度
    /// </summary>
    public OverridableIntSetting ContextSize { get; }

    /// <summary>
    /// GPU 层数
    /// </summary>
    public OverridableIntSetting GpuLayers { get; }

    /// <summary>
    /// 逻辑批大小
    /// </summary>
    public OverridableIntSetting BatchSize { get; }

    /// <summary>
    /// 物理批大小
    /// </summary>
    public OverridableIntSetting UBatchSize { get; }

    /// <summary>
    /// CPU 线程
    /// </summary>
    public OverridableIntSetting Threads { get; }

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
    /// 选了「指定层数」才显示层数输入
    /// </summary>
    public bool IsCustomGpuLayers => SelectedGpuLayerMode.Value == EGpuLayerMode.Custom;

    /// <summary>
    /// 按生效参数估算的占用与风险
    /// </summary>
    public string EstimateText
    {
        get
        {
            RuntimeLoadRisk risk = _analyzeRisk();
            return Loc.Text(LangKey.ModelParamsEstimate,
                risk.EstimatedTotalBytes > 0 ? GameUtils.FormatBytes(risk.EstimatedTotalBytes) : "-",
                RuntimeRiskLabels.Format(risk.Level));
        }
    }

    partial void OnSelectedGpuLayerModeChanged(SettingChoice<EGpuLayerMode> value)
    {
        OnPropertyChanged(nameof(IsCustomGpuLayers));
        if (_syncingGpuMode || ModeOf(GpuLayers.Value) == value.Value) return;
        GpuLayers.Value = value.Value switch
        {
            EGpuLayerMode.Auto => -1,
            EGpuLayerMode.CpuOnly => 0,
            _ => _lastCustomGpuLayers
        };
    }

    /// <summary>
    /// 全部改回跟随全局
    /// </summary>
    [RelayCommand]
    private void ResetAll()
    {
        ContextSize.Reset();
        GpuLayers.Reset();
        BatchSize.Reset();
        UBatchSize.Reset();
        Threads.Reset();
    }

    private void OnSettingChanged()
    {
        _config.SetOverrides(ModelName, new ModelRuntimeOverrides
        {
            ContextSize = ContextSize.OverrideValue is { } context ? Math.Max(0, context) : null,
            GpuLayers = GpuLayers.OverrideValue,
            BatchSize = BatchSize.OverrideValue is { } batch ? Math.Max(0, batch) : null,
            UBatchSize = UBatchSize.OverrideValue is { } uBatch ? Math.Max(0, uBatch) : null,
            Threads = Threads.OverrideValue is { } threads ? Math.Max(0, threads) : null
        });
        _save();
        OnPropertyChanged(nameof(EstimateText));
    }

    private static EGpuLayerMode ModeOf(int gpuLayers) => gpuLayers switch
    {
        < 0 => EGpuLayerMode.Auto,
        0 => EGpuLayerMode.CpuOnly,
        _ => EGpuLayerMode.Custom
    };

    private static string FormatAuto(int value) => value <= 0 ? Loc.Text(LangKey.ModelRuntimeOptionAuto) : value.ToString();

    private static string FormatGpuLayers(int value) => value switch
    {
        < 0 => Loc.Text(LangKey.ModelRuntimeGpuLayersAuto),
        0 => Loc.Text(LangKey.ModelRuntimeGpuLayersCpu),
        _ => value.ToString()
    };
}
