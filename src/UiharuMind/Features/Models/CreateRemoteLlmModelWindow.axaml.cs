using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.Extensions;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models;

public partial class CreateRemoteLlmModelWindow : Window
{
    /// <summary>
    /// 打开创建/编辑远程模型对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="remoteModelInfo">要编辑的模型,为空表示创建</param>
    /// <returns>确认时返回承载最终配置的模型信息,取消返回空</returns>
    public static async Task<RemoteModelInfo?> ShowWindow(Window owner, RemoteModelInfo? remoteModelInfo = null)
    {
        var window = new CreateRemoteLlmModelWindow
        {
            DataContext = new CreateRemoteLlmModelWindowViewModel(remoteModelInfo)
        };
        return await window.ShowDialog<RemoteModelInfo>(owner);
    }

    public CreateRemoteLlmModelWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateRemoteLlmModelWindowViewModel { CanConfirm: true } viewModel) return;
        Close(viewModel.BuildResult());
    }
}

public partial class CreateRemoteLlmModelWindowViewModel : ObservableObject
{
    private readonly RemoteModelInfo? _sourceInfo; //编辑模式的原实例,确认时写回并原样返回
    private readonly string? _originalName; //编辑模式的原名,重名校验放行用
    private BaseRemoteModelConfig? _draftConfig; //创建/复制模式下待写入的新配置实例
    private bool _suppressProviderReset; //编辑模式初始化选中服务商时不重置表单

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenProviderWebsite))]
    private ProviderItem? _selectedProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    [NotifyPropertyChangedFor(nameof(HasDuplicateNameError))]
    private string _modelName = "";

    [ObservableProperty] private string _modelPath = "";
    [ObservableProperty] private string _modelId = "";
    [ObservableProperty] private string _modelDescription = "";
    [ObservableProperty] private bool _isVision;
    [ObservableProperty] private bool _omitSamplingParams;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasApiKeyError))]
    private string _apiKey = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasContextLengthError))]
    private string _contextLengthText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasMaxTokensError))]
    private string _maxTokensText = "";

    /// <summary>
    /// 思考档位选项(与 EThinkingMode 枚举一一对应,序号即枚举序号)。
    /// 显示文案取本地化键 ThinkingMode{枚举名}
    /// </summary>
    public ThinkingModeOptionItem[] ThinkingModeOptions { get; } =
        Enum.GetValues<EThinkingMode>()
            .Select(mode => new ThinkingModeOptionItem(mode))
            .ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxTokensPlaceholder))]
    private int _thinkingModeIndex; //思考档位序号,即 EThinkingMode

    /// <summary>
    /// MaxTokens 输入框占位提示:显示该模型未填时实际会用到的默认输出预算
    /// (当前 ModelId 的预设值,没有预设才落到思考档位兜底值)
    /// </summary>
    public string MaxTokensPlaceholder
    {
        get
        {
            int defaultTokens = SelectedModelIdOption?.MaxTokens > 0
                ? SelectedModelIdOption.MaxTokens
                : BaseRemoteModelConfig.GetDefaultMaxTokens((EThinkingMode)ThinkingModeIndex);
            return defaultTokens > 0 ? defaultTokens.ToString() : string.Empty;
        }
    }

    [ObservableProperty] private bool _isVisionEditable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripFollowPreset))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripForceOn))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripForceOff))]
    private bool? _requiresReasoningContentRoundtripOverride;

    /// <summary>三选一单选组:跟随预设(覆盖为空)</summary>
    public bool IsReasoningRoundtripFollowPreset
    {
        get => RequiresReasoningContentRoundtripOverride == null;
        set { if (value) RequiresReasoningContentRoundtripOverride = null; }
    }

    /// <summary>三选一单选组:强制开启</summary>
    public bool IsReasoningRoundtripForceOn
    {
        get => RequiresReasoningContentRoundtripOverride == true;
        set { if (value) RequiresReasoningContentRoundtripOverride = true; }
    }

    /// <summary>三选一单选组:强制关闭</summary>
    public bool IsReasoningRoundtripForceOff
    {
        get => RequiresReasoningContentRoundtripOverride == false;
        set { if (value) RequiresReasoningContentRoundtripOverride = false; }
    }

    [ObservableProperty] private bool _hasModelIdOptions;
    [ObservableProperty] private bool _isPresetProvider;
    [ObservableProperty] private bool _hasCopySources;
    private bool _suppressModelIdPrefill;

    /// <summary>
    /// 可选服务商列表
    /// </summary>
    public ObservableCollection<ProviderItem> Providers { get; } = new();

    /// <summary>
    /// 当前服务商下可复制的已有远程模型
    /// </summary>
    public ObservableCollection<RemoteModelInfo> CopySources { get; } = new();

    /// <summary>
    /// 当前服务商的模型 ID 候选列表,含预设能力(视觉/默认上下文)
    /// </summary>
    public ObservableCollection<ModelIdOptionItem> ModelIdOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetRequiresReasoningContentRoundtrip))]
    [NotifyPropertyChangedFor(nameof(MaxTokensPlaceholder))]
    private ModelIdOptionItem? _selectedModelIdOption;

    /// <summary>
    /// 覆盖开关留空(跟随预设)时,当前选中模型实际会解析成的值,供界面提示用
    /// </summary>
    public bool PresetRequiresReasoningContentRoundtrip => SelectedModelIdOption?.RequiresReasoningContentRoundtrip ?? false;

    /// <summary>
    /// 上下文长度内置档位
    /// </summary>
    public string[] ContextLengthOptions { get; } =
        ["4096", "8192", "16384", "32768", "65536", "131072", "200000", "262144", "1048576"];

    /// <summary>
    /// 是否为编辑模式
    /// </summary>
    public bool IsEditMode { get; }

    /// <summary>
    /// 模型名称为空
    /// </summary>
    public bool HasNameError => string.IsNullOrWhiteSpace(ModelName);

    /// <summary>
    /// 模型名称与已有模型重复(编辑模式保持原名放行)
    /// </summary>
    public bool HasDuplicateNameError
    {
        get
        {
            var name = ModelName.Trim();
            if (name.Length == 0) return false;
            if (IsEditMode && name == _originalName) return false;
            return LlmManager.Instance.TryGetRemoteModelInfo(name, out _);
        }
    }

    /// <summary>
    /// ApiKey 为空
    /// </summary>
    public bool HasApiKeyError => string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// 上下文长度非法(留空视为未设置,合法)
    /// </summary>
    public bool HasContextLengthError
    {
        get
        {
            var text = ContextLengthText.Trim();
            return text.Length > 0 && !(int.TryParse(text, out var value) && value > 0);
        }
    }

    /// <summary>
    /// 最大输出预算非法(留空视为未设置,合法)
    /// </summary>
    public bool HasMaxTokensError
    {
        get
        {
            var text = MaxTokensText.Trim();
            return text.Length > 0 && !(int.TryParse(text, out var value) && value > 0);
        }
    }

    /// <summary>
    /// 是否允许确认
    /// </summary>
    public bool CanConfirm => !HasNameError && !HasDuplicateNameError && !HasApiKeyError &&
                              !HasContextLengthError && !HasMaxTokensError;

    /// <summary>
    /// 当前服务商是否有可跳转的官网入口
    /// </summary>
    public bool CanOpenProviderWebsite => SelectedProvider?.WebsiteUrl is { Length: > 0 };

    /// <summary>
    /// 打开当前服务商的官网(开放平台),便于查看模型文档或申请 API Key
    /// </summary>
    [RelayCommand]
    private void OpenProviderWebsite()
    {
        if (SelectedProvider?.WebsiteUrl is not { Length: > 0 } url) return;
        _ = TopLevel.GetTopLevel(App.DummyWindow)?.Launcher.LaunchUriAsync(new Uri(url));
    }

    public CreateRemoteLlmModelWindowViewModel() : this(null)
    {
    }

    public CreateRemoteLlmModelWindowViewModel(RemoteModelInfo? remoteModelInfo)
    {
        _sourceInfo = remoteModelInfo;
        IsEditMode = remoteModelInfo != null;

        var types = typeof(LlmManager).Assembly.GetTypesOfInterface(nameof(IRemoteModelConfig));
        foreach (var type in types)
        {
            var defaultConfig = Activator.CreateInstance(type) as BaseRemoteModelConfig;
            Providers.Add(new ProviderItem
            {
                Name = type.GetDescription(),
                DefaultEndpoint = defaultConfig?.ModelPath ?? "",
                WebsiteUrl = defaultConfig?.WebsiteUrl ?? "",
                ConfigType = type,
            });
        }

        Providers.Add(new ProviderItem
        {
            Name = Loc.Text(LangKey.CustomConfig),
            DefaultEndpoint = "",
            ConfigType = typeof(RemoteModelConfig),
        });

        if (remoteModelInfo == null)
        {
            SelectedProvider = Providers.FirstOrDefault();
            return;
        }

        _originalName = remoteModelInfo.ModelName;
        _suppressProviderReset = true;
        SelectedProvider = Providers.FirstOrDefault(p => p.ConfigType == remoteModelInfo.Config.GetType());
        _suppressProviderReset = false;
        LoadValues(remoteModelInfo.Config, remoteModelInfo.ApiKey, remoteModelInfo.ModelName ?? "");
        ApplyConfigTraits(remoteModelInfo.Config);
    }

    /// <summary>
    /// 将表单值一次性写入目标模型并返回。编辑模式写回并返回原实例(改名清理依赖同实例),
    /// 创建/复制模式返回持有新配置的新实例
    /// </summary>
    /// <returns>承载最终配置的模型信息</returns>
    public RemoteModelInfo BuildResult()
    {
        var target = _sourceInfo ?? new RemoteModelInfo { Config = _draftConfig! };
        var config = target.Config;
        config.ModelName = ModelName.Trim();
        config.ModelPath = ModelPath.Trim();
        config.ModelId = ModelId.Trim();
        config.ModelDescription = ModelDescription;
        config.ContextLength =
            int.TryParse(ContextLengthText.Trim(), out var contextLength) && contextLength > 0 ? contextLength : 0;
        config.MaxTokens =
            int.TryParse(MaxTokensText.Trim(), out var maxTokens) && maxTokens > 0 ? maxTokens : 0;
        config.ThinkingMode = (EThinkingMode)ThinkingModeIndex;
        if (IsVisionEditable) config.IsVision = IsVision;
        config.OmitSamplingParams = OmitSamplingParams;
        config.RequiresReasoningContentRoundtripOverride = RequiresReasoningContentRoundtripOverride;
        target.ApiKey = ApiKey;
        return target;
    }

    [RelayCommand]
    private void SelectCopySource(RemoteModelInfo source)
    {
        if (Activator.CreateInstance(source.Config.GetType()) is not BaseRemoteModelConfig config) return;
        config.Port = source.Config.Port;
        _draftConfig = config;
        LoadValues(source.Config, source.ApiKey, source.ModelName + "-copy");
        ApplyConfigTraits(config);
    }

    partial void OnSelectedProviderChanged(ProviderItem? value)
    {
        if (_suppressProviderReset || value == null) return;
        if (Activator.CreateInstance(value.ConfigType) is not BaseRemoteModelConfig config) return;
        _draftConfig = config;
        LoadValues(config, "", config.ModelName ?? "");
        ApplyConfigTraits(config);
        RefreshCopySources(value.ConfigType);
    }

    private void RefreshCopySources(Type configType)
    {
        CopySources.Clear();
        if (!IsEditMode)
        {
            foreach (var info in RemoteModelSettingConfig.Current.ModelInfos.Values)
            {
                if (info.Config.GetType() == configType) CopySources.Add(info);
            }
        }

        HasCopySources = CopySources.Count > 0;
    }

    private void LoadValues(BaseRemoteModelConfig config, string apiKey, string modelName)
    {
        ModelName = modelName;
        ModelPath = config.ModelPath ?? "";
        ModelId = config.ModelId ?? "";
        ModelDescription = config.ModelDescription ?? "";
        ContextLengthText = config.ContextLength > 0 ? config.ContextLength.ToString() : "";
        MaxTokensText = config.MaxTokens > 0 ? config.MaxTokens.ToString() : "";
        ThinkingModeIndex = (int)config.ThinkingMode;
        IsVision = config.IsVision;
        OmitSamplingParams = config.OmitSamplingParams;
        RequiresReasoningContentRoundtripOverride = config.RequiresReasoningContentRoundtripOverride;
        ApiKey = apiKey;
    }

    private void ApplyConfigTraits(BaseRemoteModelConfig config)
    {
        IsVisionEditable = ProbeIsVisionWritable(config);
        IsPresetProvider = config.GetType() != typeof(RemoteModelConfig);
        ModelIdOptions.Clear();
        foreach (string option in config.ModelIdVariants.Keys)
        {
            bool isVision = false;
            int contextLength = 0;
            int maxTokens = 0;
            bool requiresReasoningContentRoundtrip = false;
            bool omitSamplingParams = false;
            string alias = "";
            if (config.ModelIdVariants.TryGetValue(option, out var variant))
            {
                isVision = variant.IsVision;
                contextLength = variant.ContextLength;
                maxTokens = variant.MaxTokens;
                requiresReasoningContentRoundtrip = variant.RequiresReasoningContentRoundtrip;
                omitSamplingParams = variant.OmitSamplingParams;
                alias = variant.Alias;
            }

            ModelIdOptions.Add(new ModelIdOptionItem
            {
                Id = option,
                Alias = alias,
                IsVision = isVision,
                ContextLength = contextLength,
                MaxTokens = maxTokens,
                RequiresReasoningContentRoundtrip = requiresReasoningContentRoundtrip,
                OmitSamplingParams = omitSamplingParams,
            });
        }

        HasModelIdOptions = ModelIdOptions.Count > 0;

        // 让下拉框定位到当前 ModelId;创建/复制模式按预设预填默认上下文与视觉
        _suppressModelIdPrefill = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x.Id == ModelId);
        _suppressModelIdPrefill = false;

        if (!IsEditMode) ApplyModelIdPreset(SelectedModelIdOption);
    }

    /// <summary>
    /// 下拉选择模型时:同步 ModelId,并按预设默认值预填上下文与视觉状态
    /// </summary>
    /// <param name="value">选中的模型条目</param>
    partial void OnSelectedModelIdOptionChanged(ModelIdOptionItem? value)
    {
        if (value == null || _suppressModelIdPrefill) return;
        _suppressModelIdPrefill = true;
        ModelId = value.Id;
        _suppressModelIdPrefill = false;
        ApplyModelIdPreset(value);
    }

    private void ApplyModelIdPreset(ModelIdOptionItem? option)
    {
        if (option == null) return;
        if (option.ContextLength > 0) ContextLengthText = option.ContextLength.ToString();
        if (option.MaxTokens > 0) MaxTokensText = option.MaxTokens.ToString();
        if (IsVisionEditable) IsVision = option.IsVision;
        OmitSamplingParams = option.OmitSamplingParams;
        // 换了模型,之前针对旧模型的手动覆盖不该带过来——重置为"跟随预设",预设值随下拉框联动展示
        RequiresReasoningContentRoundtripOverride = null;
    }

    /// <summary>
    /// 探测 IsVision 是否可写:部分服务商以只读 override 锁定该值(如智谱 Vision),写入是空操作。
    /// 当前各配置属性均为无副作用的自动属性;若未来 setter 增加副作用,需改造此探测方式
    /// </summary>
    private static bool ProbeIsVisionWritable(BaseRemoteModelConfig config)
    {
        var original = config.IsVision;
        config.IsVision = !original;
        var writable = config.IsVision != original;
        config.IsVision = original;
        return writable;
    }

    /// <summary>
    /// 模型 ID 候选条目,携带该模型的预设能力(是否支持视觉、默认上下文)
    /// </summary>
    public sealed class ModelIdOptionItem
    {
        /// <summary>
        /// 模型 ID
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// 显示别名;为空时下拉框回退显示 <see cref="Id"/>
        /// </summary>
        public string Alias { get; init; } = "";

        /// <summary>
        /// 下拉框显示的文本
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(Alias) ? Id : Alias;

        /// <summary>
        /// 是否支持视觉
        /// </summary>
        public bool IsVision { get; init; }

        /// <summary>
        /// 默认上下文长度,0 表示未预设
        /// </summary>
        public int ContextLength { get; init; }

        /// <summary>
        /// 默认最大输出预算,0 表示未预设
        /// </summary>
        public int MaxTokens { get; init; }

        /// <summary>
        /// 思考模式下带 tool_calls 时,该模型是否要求原样带回 reasoning_content
        /// </summary>
        public bool RequiresReasoningContentRoundtrip { get; init; }

        /// <summary>
        /// 该模型是否默认不发送采样参数(temperature 等固定参数的模型,如 Kimi)
        /// </summary>
        public bool OmitSamplingParams { get; init; }

        /// <summary>
        /// 下拉框显示的文本
        /// </summary>
        public override string ToString() => Id;
    }

    /// <summary>
    /// 服务商选择列表条目
    /// </summary>
    public sealed class ProviderItem
    {
        /// <summary>
        /// 显示名称
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// 默认接口地址
        /// </summary>
        public required string DefaultEndpoint { get; init; }

        /// <summary>
        /// 服务商官网/开放平台地址,空表示无固定官网(如自定义配置)
        /// </summary>
        public string WebsiteUrl { get; init; } = string.Empty;

        /// <summary>
        /// 对应的配置类型
        /// </summary>
        public required Type ConfigType { get; init; }
    }

    /// <summary>
    /// 思考档位下拉条目:携带枚举值与本地化显示名
    /// </summary>
    public sealed class ThinkingModeOptionItem
    {
        public ThinkingModeOptionItem(EThinkingMode mode)
        {
            Mode = mode;
            Display = Loc.Text(mode switch
            {
                EThinkingMode.None => LangKey.ThinkingModeNone,
                EThinkingMode.Light => LangKey.ThinkingModeLight,
                EThinkingMode.Medium => LangKey.ThinkingModeMedium,
                EThinkingMode.High => LangKey.ThinkingModeHigh,
                EThinkingMode.Max => LangKey.ThinkingModeMax,
                _ => LangKey.ThinkingModeDefault,
            });
        }

        /// <summary>
        /// 思考档位
        /// </summary>
        public EThinkingMode Mode { get; }

        /// <summary>
        /// 本地化显示名
        /// </summary>
        public string Display { get; }

        /// <summary>
        /// 下拉框默认显示的文本
        /// </summary>
        public override string ToString() => Display;
    }
}
