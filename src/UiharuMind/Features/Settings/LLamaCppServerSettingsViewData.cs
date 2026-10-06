using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Generated;
using UiharuMind.Shared.Data;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// llama-server 专有启动选项的界面数据。任一项改动即整份写回并落盘，下次加载模型生效
/// </summary>
public partial class LLamaCppServerSettingsViewData : ObservableObject
{
    private readonly LLamaCppSettingConfig _config;
    private readonly SettingsWriteBack _writeBack;

    [ObservableProperty] private bool _fit;
    [ObservableProperty] private decimal _fitTargetMiB;
    [ObservableProperty] private decimal _threadsBatch;
    [ObservableProperty] private decimal _parallel;
    [ObservableProperty] private bool _continuousBatching;
    [ObservableProperty] private SettingChoice<string> _cacheTypeK;
    [ObservableProperty] private SettingChoice<string> _cacheTypeV;
    [ObservableProperty] private SettingChoice<string> _loadMode;
    [ObservableProperty] private bool _noKvOffload;
    [ObservableProperty] private decimal _cpuMoeLayers;
    [ObservableProperty] private decimal _cacheRamMiB;
    [ObservableProperty] private decimal _cacheReuse;
    [ObservableProperty] private bool _contextShift;
    [ObservableProperty] private bool _swaFull;
    [ObservableProperty] private decimal _loadTimeoutSeconds;
    [ObservableProperty] private string _environmentVariables;
    [ObservableProperty] private string _extraArguments;

    public LLamaCppServerSettingsViewData() : this(LLamaCppSettingConfig.Current)
    {
    }

    /// <param name="config">llama.cpp 设置（测试传独立实例）</param>
    public LLamaCppServerSettingsViewData(LLamaCppSettingConfig config)
    {
        _config = config;
        _writeBack = new SettingsWriteBack(config.Save);
        CacheTypeOptions = LLamaCppServerOptions.CacheTypes
            .Select(x => new SettingChoice<string>(x, x == LLamaCppServerOptions.DefaultCacheType
                ? Loc.Text(LangKey.LLamaCppDefaultSuffix, x)
                : x))
            .ToList();
        LoadModeOptions = LLamaCppServerOptions.LoadModes
            .Select(x => new SettingChoice<string>(x, Loc.Text("LLamaCppLoadMode_" + x.Replace("+", "_"))))
            .ToList();
        _loadMode = LoadModeOptions[0];
        _cacheTypeK = CacheTypeOptions[0];
        _cacheTypeV = CacheTypeOptions[0];
        _environmentVariables = "";
        _extraArguments = "";
        Load(config.Server);
    }

    /// <summary>
    /// KV 缓存类型可选项
    /// </summary>
    public IReadOnlyList<SettingChoice<string>> CacheTypeOptions { get; }

    /// <summary>
    /// 加载方式可选项
    /// </summary>
    public IReadOnlyList<SettingChoice<string>> LoadModeOptions { get; }

    /// <summary>
    /// 恢复 llama-server 选项的默认值
    /// </summary>
    public void ResetToDefaults()
    {
        Load(new LLamaCppServerOptions());
        Apply();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_writeBack.IsLoading) Apply();
    }

    private void Load(LLamaCppServerOptions options)
    {
        using (_writeBack.BeginLoad())
        {
            Fit = options.Fit;
            FitTargetMiB = options.FitTargetMiB;
            ThreadsBatch = options.ThreadsBatch;
            Parallel = options.Parallel;
            ContinuousBatching = options.ContinuousBatching;
            CacheTypeK = Choice(options.CacheTypeK);
            CacheTypeV = Choice(options.CacheTypeV);
            LoadMode = LoadModeOptions.FirstOrDefault(x => x.Value == options.LoadMode) ?? LoadModeOptions[0];
            NoKvOffload = options.NoKvOffload;
            CpuMoeLayers = options.CpuMoeLayers;
            CacheRamMiB = options.CacheRamMiB;
            CacheReuse = options.CacheReuse;
            ContextShift = options.ContextShift;
            SwaFull = options.SwaFull;
            LoadTimeoutSeconds = options.LoadTimeoutSeconds;
            EnvironmentVariables = options.EnvironmentVariables;
            ExtraArguments = options.ExtraArguments;
        }
    }

    // 界面上的值整份写回：字段多，逐个写 OnXChanged 只是同一句话抄十几遍
    private void Apply()
    {
        _config.Server = new LLamaCppServerOptions
        {
            Fit = Fit,
            FitTargetMiB = (int)FitTargetMiB,
            ThreadsBatch = (int)ThreadsBatch,
            Parallel = (int)Parallel,
            ContinuousBatching = ContinuousBatching,
            CacheTypeK = CacheTypeK.Value,
            CacheTypeV = CacheTypeV.Value,
            LoadMode = LoadMode.Value,
            NoKvOffload = NoKvOffload,
            CpuMoeLayers = (int)CpuMoeLayers,
            CacheRamMiB = (int)CacheRamMiB,
            CacheReuse = (int)CacheReuse,
            ContextShift = ContextShift,
            SwaFull = SwaFull,
            LoadTimeoutSeconds = (int)LoadTimeoutSeconds,
            EnvironmentVariables = EnvironmentVariables.Trim(),
            ExtraArguments = ExtraArguments.Trim()
        };
        _writeBack.Save();
    }

    private SettingChoice<string> Choice(string value) =>
        CacheTypeOptions.FirstOrDefault(x => x.Value == value) ?? CacheTypeOptions[0];
}
