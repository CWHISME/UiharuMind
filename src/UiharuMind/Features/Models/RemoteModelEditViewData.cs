using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.Extensions;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models;

/// <summary>
/// 远程对话模型的新建、编辑、复制表单：服务商预设回填、模型 ID 候选、重名与必填校验，确认时产出模型信息
/// </summary>
public partial class RemoteModelEditViewData : ObservableObject
{
    private readonly RemoteModelInfo? _sourceInfo; //编辑模式的原实例,确认时写回并原样返回
    private readonly string? _originalName; //编辑模式的原名,重名校验放行用
    private readonly Func<string, bool> _isNameTaken; //名字是否已被别的模型占用
    private BaseRemoteModelConfig? _draftConfig; //创建/复制模式下待写入的新配置实例
    private bool _suppressProviderReset; //编辑模式初始化选中服务商时不重置表单
    private readonly IModelCatalogFetcher _catalogFetcher; //模型目录拉取器（测试注入 fake）
    private readonly IModelCatalogCache _catalogCache; //模型目录缓存（测试注入 fake）

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenProviderWebsite))]
    [NotifyPropertyChangedFor(nameof(HasCatalog))]
    private ProviderItem? _selectedProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    [NotifyPropertyChangedFor(nameof(HasDuplicateNameError))]
    private string _modelName = "";

    [ObservableProperty] private string _modelPath = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasModelIdError))]
    private string _modelId = "";
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

    [ObservableProperty] private int _thinkingModeIndex; //思考档位序号,即 EThinkingMode

    /// <summary>
    /// MaxTokens 输入框占位提示:显示该模型未填时实际会用到的默认输出预算
    /// (当前 ModelId 的预设值,没有预设才落到兜底值)
    /// </summary>
    public string MaxTokensPlaceholder =>
        SelectedModelIdOption is { MaxTokens: > 0 } option
            ? option.MaxTokens.ToString()
            : BaseRemoteModelConfig.DefaultMaxTokens.ToString();

    [ObservableProperty] private bool _isVisionEditable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripAutoDetect))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripForceOn))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripForceOff))]
    private bool? _requiresReasoningContentRoundtripOverride;

    /// <summary>三选一单选组:自动检测(覆盖为空,有思考就回传)</summary>
    public bool IsReasoningRoundtripAutoDetect
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
    private bool _suppressModelNamePrefill; //LoadValues/自动同步写名字时不把用户改动标记置真
    private bool _modelNameEditedByUser; //用户是否手改过模型名（决定自动同步是否放行）
    private bool _catalogRefreshPending; //强制刷新撞上在飞的拉取时先记下，等这轮结束补跑

    /// <summary>目录候选的显示集合：跟随 ModelId 文本过滤，空文本显示全量（ADR 0092）</summary>
    public ObservableCollection<ModelIdOptionItem> VisibleModelIdOptions { get; } = new();

    /// <summary>自定义服务商的目录地址（表单字段，不随模型存档）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalog))]
    private string _catalogUrlText = "";

    [ObservableProperty] private bool _isFetchingCatalog;

    /// <summary>目录拉取失败提示（空=无提示）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CatalogFetchErrorVisible))]
    private string _catalogFetchError = "";

    /// <summary>当前服务商是否有模型目录地址（决定「拉取」按钮与自动拉取）</summary>
    public bool HasCatalog => EffectiveCatalogUrl.Length > 0;

    /// <summary>目录拉取失败提示是否可见</summary>
    public bool CatalogFetchErrorVisible => CatalogFetchError.Length > 0;

    /// <summary>生效的目录地址：预设读常量，自定义读表单字段</summary>
    private string EffectiveCatalogUrl => IsPresetProvider
        ? SelectedProvider?.CatalogUrl ?? ""
        : CatalogUrlText.Trim();

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
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripPresetAuto))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripPresetOff))]
    [NotifyPropertyChangedFor(nameof(MaxTokensPlaceholder))]
    private ModelIdOptionItem? _selectedModelIdOption;

    /// <summary>覆盖开关留空(自动检测)时,当前选中模型的预设判断:要求回传,供界面提示用</summary>
    public bool PresetRequiresReasoningContentRoundtrip => SelectedModelIdOption?.RequiresReasoningContentRoundtrip == true;

    /// <summary>当前选中模型的预设判断:自动检测(未声明,有思考就回传)</summary>
    public bool IsReasoningRoundtripPresetAuto => SelectedModelIdOption?.RequiresReasoningContentRoundtrip == null;

    /// <summary>当前选中模型的预设判断:不回传</summary>
    public bool IsReasoningRoundtripPresetOff => SelectedModelIdOption?.RequiresReasoningContentRoundtrip == false;

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
            return _isNameTaken(name);
        }
    }

    /// <summary>
    /// 模型 ID 为空
    /// </summary>
    public bool HasModelIdError => string.IsNullOrWhiteSpace(ModelId);

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
    public bool CanConfirm => !HasNameError && !HasDuplicateNameError && !HasModelIdError && !HasApiKeyError &&
                              !HasContextLengthError && !HasMaxTokensError;

    /// <summary>
    /// 当前服务商是否有可跳转的官网入口
    /// </summary>
    public bool CanOpenProviderWebsite => SelectedProvider?.WebsiteUrl is { Length: > 0 };

    /// <summary>新建模式，视为没有已占用的名字（供设计期预览与测试）</summary>
    public RemoteModelEditViewData() : this(null, _ => false, new ModelCatalogFetcher(), new ModelCatalogCache())
    {
    }

    /// <summary>
    /// 新建或编辑
    /// </summary>
    /// <param name="remoteModelInfo">要编辑的模型；null 为新建</param>
    /// <param name="isNameTaken">名字是否已被已有模型占用（重名校验用）</param>
    /// <param name="catalogFetcher">模型目录拉取器；缺省用真实实现（测试注入 fake）</param>
    /// <param name="catalogCache">模型目录缓存；缺省用真实实现（测试注入 fake）</param>
    public RemoteModelEditViewData(RemoteModelInfo? remoteModelInfo, Func<string, bool> isNameTaken,
        IModelCatalogFetcher? catalogFetcher = null, IModelCatalogCache? catalogCache = null)
    {
        _isNameTaken = isNameTaken;
        _sourceInfo = remoteModelInfo;
        IsEditMode = remoteModelInfo != null;
        _catalogFetcher = catalogFetcher ?? new ModelCatalogFetcher();
        _catalogCache = catalogCache ?? new ModelCatalogCache();

        foreach (Type type in RemoteModelConfigRegistry.ProviderTypes)
        {
            BaseRemoteModelConfig? defaultConfig = RemoteModelConfigRegistry.CreateDefault(type);
            Providers.Add(new ProviderItem
            {
                Name = type.GetDescription(),
                DefaultEndpoint = defaultConfig?.ModelPath ?? "",
                WebsiteUrl = defaultConfig?.WebsiteUrl ?? "",
                CatalogUrl = defaultConfig?.ModelCatalogUrl ?? "",
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
        _modelNameEditedByUser = !string.IsNullOrWhiteSpace(ModelName); //已存名字是用户定的，自动同步不再顶掉
        ApplyConfigTraits(remoteModelInfo.Config);

        // 编辑模式初始化也要吃目录候选（如 CommandCode 已存档再编辑），但不覆盖已存 ModelId
        TryAutoLoadCatalog();
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
        string previousModelPath = config.ModelPath;
        config.ModelName = ModelName.Trim();
        config.ModelPath = ModelPath.Trim();
        // 换了端点/服务商:旧端点的思考字段观测值整份作废,等新端点重新观测(ADR 0093)。
        // 同端点切 ModelId 不用清:观测值本身按 ModelId 分开记,不会串到别的模型上
        if (!string.Equals(previousModelPath, config.ModelPath, StringComparison.Ordinal)) config.ObservedThinkingFields.Clear();
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
        // 目录项带 wire 信息：选中时按端点写入 ApiFormat；手输/未选不碰（保留旧值/默认 ChatCompletions，ADR 0092）
        if (SelectedModelIdOption is { } selected) config.ApiFormat = selected.ApiFormat;
        target.ApiKey = ApiKey;
        return target;
    }

    [RelayCommand]
    private void SelectCopySource(RemoteModelInfo source)
    {
        if (RemoteModelConfigRegistry.CreateDefault(source.Config.GetType()) is not BaseRemoteModelConfig config) return;
        config.Port = source.Config.Port;
        _draftConfig = config;
        LoadValues(source.Config, source.ApiKey, source.ModelName + "-copy");
        // 复制名是程序生成的占位，不算用户手改：之后选模型 id 时名字照常跟随（用户亲自改过才锁定）
        _modelNameEditedByUser = false;
        ApplyConfigTraits(config);
    }

    partial void OnSelectedProviderChanged(ProviderItem? value)
    {
        if (_suppressProviderReset || value == null) return;
        if (RemoteModelConfigRegistry.CreateDefault(value.ConfigType) is not BaseRemoteModelConfig config) return;
        _draftConfig = config;
        LoadValues(config, "", config.ModelName ?? "");
        _modelNameEditedByUser = false; //切服务商=新表单：预设默认名不算用户手改
        ApplyConfigTraits(config);
        RefreshCopySources(value.ConfigType);

        // 目录地址是服务商属性：换服务商清掉自定义字段，有目录则按「有缓存不自动拉」加载（ADR 0092）
        CatalogUrlText = "";
        CatalogFetchError = "";
        TryAutoLoadCatalog();
    }

    partial void OnCatalogUrlTextChanged(string value)
    {
        if (IsPresetProvider) return;
        // 只对「看起来完整的 http(s) 地址」自动拉，避免击键到一半就发请求（Spec 评审 b-2）
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        _ = LoadCatalogAsync(force: false);
    }

    /// <summary>有目录地址时按「有缓存不自动拉」口径加载（无缓存才拉网络）</summary>
    private void TryAutoLoadCatalog()
    {
        if (EffectiveCatalogUrl.Length > 0) _ = LoadCatalogAsync(force: false);
    }

    /// <summary>手动「拉取」：强制走网络并覆盖缓存（ADR 0092）</summary>
    [RelayCommand]
    private Task RefreshCatalog() => LoadCatalogAsync(force: true);

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
        // 初始化回填途中不触发按 ModelId 的预填(OnModelIdChanged 会 ApplyModelIdPreset):
        // 编辑模式要把用户已保存的手动设置原样带回,创建模式的初始预填由 ApplyConfigTraits 末尾显式做
        _suppressModelIdPrefill = true;
        try
        {
            _suppressModelNamePrefill = true;
            ModelName = modelName;
            _suppressModelNamePrefill = false;
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
        finally
        {
            _suppressModelIdPrefill = false;
        }
    }

    private void ApplyConfigTraits(BaseRemoteModelConfig config)
    {
        // 注意:下面的 ModelIdOptions.Clear() 会触发 ItemsSource 的 Reset,控件侧会把
        // Text 置空并经双向绑定回写 VM(复制模型时源 ID 就是这么丢的)。先留底再重建。
        var currentId = ModelId;
        IsVisionEditable = ProbeIsVisionWritable(config);
        IsPresetProvider = config.GetType() != typeof(RemoteModelConfig);
        ModelIdOptions.Clear();
        foreach (string option in config.ModelIdVariants.Keys)
        {
            bool isVision = false;
            int contextLength = 0;
            int maxTokens = 0;
            bool? requiresReasoningContentRoundtrip = null;
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

        // 让下拉框定位到当前 ModelId;创建/复制模式按预设预填默认上下文与视觉。
        // 重建期间被回写冲掉的文本在这里补回去(见方法头的注释)。
        _suppressModelIdPrefill = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x.Id == currentId);
        ModelId = currentId;
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
        TrySyncModelName(value);
    }

    /// <summary>
    /// 模型目录加载：有缓存先展示且不再自动拉（Q12 修订）；无缓存才自动拉；「拉取」按钮强制拉并覆盖。
    /// 失败时保留已有列表并提示（无列表则空 + 提示，仍可手输）。
    /// </summary>
    private async Task LoadCatalogAsync(bool force)
    {
        if (IsFetchingCatalog)
        {
            // 自动拉取在飞时点「拉取」不该什么都不发生：记下来，等这轮结束再补一次强制刷新
            if (force) _catalogRefreshPending = true;
            return;
        }

        string url = EffectiveCatalogUrl;
        if (url.Length == 0) return;

        IsFetchingCatalog = true;
        CatalogFetchError = "";
        try
        {
            IReadOnlyList<ModelCatalogEntry>? cached = _catalogCache.TryRead(url);
            if (cached != null)
            {
                ApplyCatalog(cached);
                if (!force) return; // 有缓存不自动拉（空列表缓存也是缓存，Q12 修订）
            }

            IReadOnlyList<ModelCatalogEntry> fetched = await _catalogFetcher.FetchAsync(url, ApiKey).ConfigureAwait(true);
            _catalogCache.Write(url, fetched);
            // 拉取期间用户可能已切走服务商：这一份属于旧地址，灌进去就是串台
            if (string.Equals(url, EffectiveCatalogUrl, StringComparison.Ordinal)) ApplyCatalog(fetched);
        }
        catch (Exception e)
        {
            Log.Warning($"model catalog fetch failed for '{url}': {e.GetType().Name}: {e.Message}");
            CatalogFetchError = Loc.Text(LangKey.RemoteModelCatalogFetchFailed);
        }
        finally
        {
            IsFetchingCatalog = false;
            if (_catalogRefreshPending)
            {
                _catalogRefreshPending = false;
                await LoadCatalogAsync(force: true).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// 把目录条目灌进候选列表：v1 只显示 chat/completions 线（/messages-only 的 Claude 系过滤），
    /// 重建期间保护已选/已填的 ModelId 不被回写冲掉（同 ApplyConfigTraits 的注释）。
    /// </summary>
    private void ApplyCatalog(IReadOnlyList<ModelCatalogEntry> entries)
    {
        string currentId = ModelId;
        IReadOnlyDictionary<string, RemoteModelIdVariant> presets =
            _draftConfig?.ModelIdVariants ?? RemoteModelIdVariant.Empty;
        string providerKey = _draftConfig is null ? "" : ModelCatalogCapabilities.ProviderKeyOf(_draftConfig.GetType());
        ModelIdOptions.Clear();
        HashSet<string> seen = new(StringComparer.Ordinal); //目录偶有重复 id，重复项会让下拉出现同一条两次
        foreach (ModelCatalogEntry entry in entries)
        {
            // 端点字段缺失（OpenRouter 等标准 /v1/models 不返回）默认展示；
            // 只有显式声明且不含 /chat/completions（即 /messages-only 的 Claude 系）才过滤（ADR 0092）
            if (entry.SupportedEndpoints.Count > 0 && !entry.SupportsEndpoint("/chat/completions")) continue;
            // 出图模型（如 sensenova-u1.5-lite）不是对话候选；输出模态未声明时按对话模型处理
            if (!entry.SupportsTextOutput) continue;
            if (!seen.Add(entry.Id)) continue;

            presets.TryGetValue(entry.Id, out RemoteModelIdVariant? preset);
            ResolvedCapabilities capabilities = ModelCatalogCapabilities.Resolve(entry, preset, providerKey);
            ModelIdOptions.Add(new ModelIdOptionItem
            {
                Id = entry.Id,
                Alias = entry.Name.Length > 0 ? entry.Name : preset?.Alias ?? "",
                IsVision = capabilities.IsVision,
                ContextLength = capabilities.ContextLength,
                MaxTokens = capabilities.MaxTokens,
                OmitSamplingParams = capabilities.OmitSamplingParams,
                RequiresReasoningContentRoundtrip = preset?.RequiresReasoningContentRoundtrip,
                ApiFormat = EApiFormat.ChatCompletions,
            });
        }

        HasModelIdOptions = ModelIdOptions.Count > 0;
        _suppressModelIdPrefill = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x.Id == currentId);
        ModelId = currentId;
        _suppressModelIdPrefill = false;
        RefreshVisibleModelIdOptions();
        if (!IsEditMode) ApplyModelIdPreset(SelectedModelIdOption);
    }

    /// <summary>按 ModelId 文本过滤可见候选（id 或显示名，忽略大小写子串；空文本=全量）</summary>
    private void RefreshVisibleModelIdOptions()
    {
        string filter = ModelId.Trim();
        VisibleModelIdOptions.Clear();
        foreach (ModelIdOptionItem option in ModelIdOptions)
        {
            if (filter.Length == 0 ||
                option.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                option.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                VisibleModelIdOptions.Add(option);
            }
        }
    }

    /// <summary>
    /// 手输/代码改了模型 ID 文本时:把下拉选中项对齐到同一条。
    /// 可编辑下拉框的 <c>Text</c> 与 <c>SelectedItem</c> 是双向绑定的两条道,
    /// 两边值一旦不一致(如复制模型先写文本后定选中),控件回写就会把文本冲掉——
    /// VM 里永远保持两者一致,那次回写写回来的就是同一个值,等于没写。
    /// 对齐后若命中预设表,再按预设预填视觉/采样参数等强相关选项(与下拉选择同一口径);
    /// 不命中(自定义 ID)不碰,手输自定义 ID 不该被预设覆盖。
    /// </summary>
    /// <param name="value">新的模型 ID 文本</param>
    partial void OnModelIdChanged(string value)
    {
        if (_suppressModelIdPrefill) return;
        _suppressModelIdPrefill = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x.Id == value);
        _suppressModelIdPrefill = false;
        if (SelectedModelIdOption != null)
        {
            ApplyModelIdPreset(SelectedModelIdOption);
            TrySyncModelName(SelectedModelIdOption);
        }
        RefreshVisibleModelIdOptions();
    }

    partial void OnModelNameChanged(string value)
    {
        if (_suppressModelNamePrefill) return;
        _modelNameEditedByUser = true;
    }

    /// <summary>
    /// 选中模型项时让模型名跟着走：用户没手改过、或名字为空时才写。
    /// 名字取显示名；没有显示名（Alias 空，如预设变体表、无 name 字段的目录）回退用模型 ID 本身。
    /// </summary>
    private void TrySyncModelName(ModelIdOptionItem option)
    {
        if (_modelNameEditedByUser && !string.IsNullOrWhiteSpace(ModelName)) return;
        _suppressModelNamePrefill = true;
        ModelName = option.Alias.Length > 0 ? option.Alias : option.Id;
        _suppressModelNamePrefill = false;
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
}
