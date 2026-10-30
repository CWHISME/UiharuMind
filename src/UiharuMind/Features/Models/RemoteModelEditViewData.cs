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
using UiharuMind.Core.AI.Net.Transport;
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
    [NotifyPropertyChangedFor(nameof(SelectedCapabilitySummary))]
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
    /// 思考档位候选：跟随当前模型声明的值重建（<see cref="BaseRemoteModelConfig.SupportedThinkingEfforts"/>），
    /// 固定以「不干预」「关闭」两项打头。模型声明了 <c>xhigh</c> 之类就只给声明的，
    /// 不再固定 low/medium/high/max 四档。
    /// </summary>
    public ObservableCollection<ThinkingOptionItem> ThinkingOptions { get; } = new();

    [ObservableProperty] private ThinkingOptionItem? _selectedThinkingOption;

    private string _thinkingEffortValue = ThinkingEfforts.Auto; //当前选中的 wire 值,重建选项时按它恢复
    private bool _suppressThinkingOptionWrite; //重建候选时不回写值

    /// <summary>
    /// MaxTokens 输入框占位提示:显示该模型未填时实际会用到的默认输出预算
    /// (当前 ModelId 的预设值,没有预设才落到兜底值)
    /// </summary>
    public string MaxTokensPlaceholder =>
        SelectedModelIdOption is { MaxTokens: > 0 } option
            ? option.MaxTokens.ToString()
            : BaseRemoteModelConfig.DefaultMaxTokens.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripAutoDetect))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripForceOn))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripForceOff))]
    private bool? _replayExistingReasoningOverride;

    /// <summary>三选一单选组:自动检测(覆盖为空,有思考就回传)</summary>
    public bool IsReasoningRoundtripAutoDetect
    {
        get => ReplayExistingReasoningOverride == null;
        set { if (value) ReplayExistingReasoningOverride = null; }
    }

    /// <summary>三选一单选组:强制开启</summary>
    public bool IsReasoningRoundtripForceOn
    {
        get => ReplayExistingReasoningOverride == true;
        set { if (value) ReplayExistingReasoningOverride = true; }
    }

    /// <summary>三选一单选组:强制关闭</summary>
    public bool IsReasoningRoundtripForceOff
    {
        get => ReplayExistingReasoningOverride == false;
        set { if (value) ReplayExistingReasoningOverride = false; }
    }

    [ObservableProperty] private bool _hasModelIdOptions;
    [ObservableProperty] private bool _isPresetProvider;
    [ObservableProperty] private bool _hasCopySources;
    private bool _suppressModelIdPrefill;
    private bool _suppressModelNamePrefill; //LoadValues/自动同步写名字时不把用户改动标记置真
    private bool _modelNameEditedByUser; //用户是否手改过模型名（决定自动同步是否放行）
    private bool _catalogRefreshPending; //强制刷新撞上在飞的拉取时先记下，等这轮结束补跑
    private bool _modelPathEditedByUser; //用户手改过接口地址：之后不再跟着模型推荐跑，直到他重新选模型
    private bool _suppressModelPathDirty; //程序化改地址时不标记为手动编辑

    /// <summary>目录候选的显示集合：跟随 ModelId 文本过滤，空文本显示全量（ADR 0092）</summary>
    public ObservableCollection<ModelIdOptionItem> VisibleModelIdOptions { get; } = new();

    /// <summary>候选排序维度（默认保持目录原序）</summary>
    public ModelSortOption[] SortOptions { get; }

    [ObservableProperty] private ModelSortOption _selectedSortOption;

    partial void OnSelectedSortOptionChanged(ModelSortOption value) => RefreshVisibleModelIdOptions();

    /// <summary>
    /// 至少有一条候选公布了单价——排序头据此决定出不出现。
    /// 没有价格可排的供应商（sensenova 之类）不该看到一个点了没反应的排序开关。
    /// </summary>
    [ObservableProperty] private bool _hasSortablePrices;

    /// <summary>模型 ID 下拉（候选浮层）是否展开</summary>
    [ObservableProperty] private bool _isModelPickerOpen;

    /// <summary>
    /// 接口地址候选：按当前模型的数据给出的完整端点。目录声明优先；没声明时 Claude 走 /messages、
    /// 其余按 chat / responses / messages 列出。与 <see cref="ModelPath"/> 是同一个字段的两面——
    /// 选哪个地址就是哪个格式，不另存格式
    /// </summary>
    public ObservableCollection<string> EndpointOptions { get; } = new();

    /// <summary>
    /// 按当前模型数据重建地址候选。只在候选真的变了的时候才动 <see cref="EndpointOptions"/>——
    /// 清空一个可编辑 ComboBox 的 ItemsSource 会把它框里的文本一并清掉并回写，
    /// 每次按键都重建等于每次都跟用户抢一次光标
    /// </summary>
    /// <param name="applyRecommendation">是否把地址切到推荐值：只在「用户选了模型」时传 true，
    /// 初始化与复制只重建候选、不动地址</param>
    private void RefreshEndpointOptions(bool applyRecommendation)
    {
        string current = ModelPath;
        _suppressModelPathDirty = true;
        try
        {
            List<string> candidates = [];
            foreach (EApiFormat format in CandidateFormats())
            {
                string candidate = CandidateAddress(current, format);
                // 候选必须能反推回该格式，否则用户选了它也只会落成别的线
                if (candidate.Length == 0 || RemoteEndpoints.FormatOf(candidate) != format) continue;
                if (!candidates.Contains(candidate)) candidates.Add(candidate);
            }

            if (!candidates.SequenceEqual(EndpointOptions))
            {
                EndpointOptions.Clear();
                foreach (string candidate in candidates) EndpointOptions.Add(candidate);
                // 重建把框里的文本清空了才补回去；没清就一个字节都不动
                if (ModelPath.Length == 0) ModelPath = current;
            }

            if (applyRecommendation) ApplyRecommendedEndpoint();
        }
        finally
        {
            _suppressModelPathDirty = false;
        }
    }

    /// <summary>
    /// 这个模型该给哪几条地址。
    /// 目录声明了端点就只给声明的；没声明时 Claude 固定 messages；
    /// 其余「有目录的服务商与自定义网关」三条都给，单线预设只给它自己那条
    /// （给 DeepSeek 摆一个 <c>/messages</c> 出来，点一下就是 404）
    /// </summary>
    private IReadOnlyList<EApiFormat> CandidateFormats()
    {
        if (SelectedModelIdOption is { ApiFormats.Count: > 0 } option) return option.ApiFormats;
        if (IsClaudeModel) return [EApiFormat.Messages];
        return IsPresetProvider && !HasCatalog
            ? [RemoteEndpoints.FormatOf(ModelPath) ?? EApiFormat.ChatCompletions]
            : RemoteEndpoints.All;
    }

    /// <summary>当前模型 ID 是不是 Claude 系（这类模型走 Anthropic Messages 线）</summary>
    private bool IsClaudeModel => ModelId.Contains("claude", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把地址换成指定格式的完整端点。供应商声明了各条线的端点就用它的（OpenCode 那种前缀不同的），
    /// 否则借运行时的归一规则换尾段——保证「界面给的候选」和「真打出去的地址」是同一个
    /// </summary>
    /// <param name="address">当前地址</param>
    /// <param name="format">目标格式</param>
    /// <returns>该格式的完整端点地址；地址还不成形时原样返回</returns>
    private string CandidateAddress(string address, EApiFormat format)
    {
        string trimmed = address.Trim();
        if (CurrentConfig is { } config)
        {
            string declared = config.EndpointFor(format);
            // 用户把地址改成了别家网关（不是本供应商声明的那几条）时按他的地址走，别拿预设覆盖他
            if (declared.Length > 0 && (trimmed.Length == 0 || IsDeclaredEndpoint(config, trimmed))) return declared;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out _)
            ? RemoteEndpoints.EndpointFor(trimmed, format)
            : address;
    }

    /// <summary>这个地址是不是本供应商声明的那几条端点之一</summary>
    private static bool IsDeclaredEndpoint(BaseRemoteModelConfig config, string address) =>
        Enum.GetValues<EApiFormat>().Any(format =>
            string.Equals(config.EndpointFor(format), address, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 把地址切到当前模型的推荐端点：目录声明优先（声明里有 /messages 的 Claude 走 messages），
    /// 没声明时 Claude 走 messages、其余 chat。编辑模式、用户手改过地址、以及没有模型数据可依时都不动
    /// </summary>
    private void ApplyRecommendedEndpoint()
    {
        if (IsEditMode || _modelPathEditedByUser) return;
        if (!IsClaudeModel && SelectedModelIdOption is null) return; //纯自定义 ID：地址是用户填的
        EApiFormat recommended = SelectedModelIdOption?.RecommendedFormat
                                 ?? (IsClaudeModel ? EApiFormat.Messages : EApiFormat.ChatCompletions);
        string target = CandidateAddress(ModelPath, recommended);
        if (RemoteEndpoints.FormatOf(target) != recommended) return; //换不出来就别改，留给用户
        _suppressModelPathDirty = true;
        try
        {
            ModelPath = target;
        }
        finally
        {
            _suppressModelPathDirty = false;
        }
    }

    /// <summary>
    /// 用户改地址：记为手动编辑（之后不再被模型推荐顶掉），并在还没有候选时补一次
    /// ——自定义服务商一开始地址是空的，填成完整地址后才有东西可列
    /// </summary>
    partial void OnModelPathChanged(string value)
    {
        if (_suppressModelPathDirty) return;
        _modelPathEditedByUser = true;
        if (EndpointOptions.Count == 0) RefreshEndpointOptions(applyRecommendation: false);
    }

    /// <summary>
    /// 点选候选：写入 ID、按预设预填，然后收起浮层。
    /// 同一项重复点不会触发 <see cref="OnSelectedModelIdOptionChanged"/>，但手输过的文本仍要拉回来。
    /// </summary>
    /// <param name="option">被点选的候选</param>
    public void SelectModelIdOption(ModelIdOptionItem option)
    {
        // 点候选＝明确「按这个模型来」：之前手改的地址不再拦着推荐
        _modelPathEditedByUser = false;
        SelectedModelIdOption = option;
        if (ModelId != option.Id) ModelId = option.Id;
        // 同一项再点一次不会触发 OnModelIdChanged，但用户的意图是「回到它的默认端点」
        else RefreshEndpointOptions(applyRecommendation: true);
        IsModelPickerOpen = false;
    }

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
    [NotifyPropertyChangedFor(nameof(PresetReplaysExistingReasoning))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripPresetAuto))]
    [NotifyPropertyChangedFor(nameof(IsReasoningRoundtripPresetOff))]
    [NotifyPropertyChangedFor(nameof(MaxTokensPlaceholder))]
    [NotifyPropertyChangedFor(nameof(SelectedCapabilitySummary))]
    private ModelIdOptionItem? _selectedModelIdOption;

    /// <summary>
    /// 当前模型 ID 的能力摘要：上下文、挂牌单价、声明的思考档位。
    /// 手输的 ID 不在候选里也照查（价格与档位本来就是查表来的），查不到就明说"未公布"，
    /// 不写 0 也不留空——空着会被读成"免费"
    /// </summary>
    public string SelectedCapabilitySummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ModelId)) return "";
            List<string> parts = [];
            ModelIdOptionItem? option = SelectedModelIdOption;
            if (option is { ContextLength: > 0 }) parts.Add(option.ContextDisplay);

            ModelPrice? price = option?.Price ?? ResolvePrice(ModelId);
            parts.Add(price is { IsKnown: true } known
                ? string.Format(Loc.Text(LangKey.RemoteModelPriceFormat), known.Summary)
                : Loc.Text(LangKey.RemoteModelPriceUnknown));

            string efforts = option is { Efforts.Length: > 0 } ? option.Efforts : ResolveEfforts(ModelId);
            if (efforts.Length > 0)
                parts.Add(string.Format(Loc.Text(LangKey.RemoteModelEffortsFormat), efforts));

            return string.Join(" · ", parts);
        }
    }

    /// <summary>覆盖开关留空(自动检测)时,当前选中模型的预设判断:要求回传,供界面提示用</summary>
    public bool PresetReplaysExistingReasoning => SelectedModelIdOption?.ReplayExistingReasoning == true;

    /// <summary>当前选中模型的预设判断:自动检测(未声明,有思考就回传)</summary>
    public bool IsReasoningRoundtripPresetAuto => SelectedModelIdOption?.ReplayExistingReasoning == null;

    /// <summary>当前选中模型的预设判断:不回传</summary>
    public bool IsReasoningRoundtripPresetOff => SelectedModelIdOption?.ReplayExistingReasoning == false;

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
        SortOptions =
        [
            new ModelSortOption { Sort = EModelPriceSort.Default, Display = Loc.Text(LangKey.RemoteModelSortDefault) },
            new ModelSortOption { Sort = EModelPriceSort.Input, Display = Loc.Text(LangKey.RemoteModelSortInput) },
            new ModelSortOption { Sort = EModelPriceSort.Output, Display = Loc.Text(LangKey.RemoteModelSortOutput) },
            new ModelSortOption
            {
                Sort = EModelPriceSort.CacheRead, Display = Loc.Text(LangKey.RemoteModelSortCacheRead)
            },
        ];
        _selectedSortOption = SortOptions[0]; //直接写字段：构造期不必触发一次列表重建

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
        // 换了端点/服务商:旧端点学到的思考口径整份作废,等新端点重新学(ADR 0093/0095)。
        // 同端点切 ModelId 不用清:两者都按 ModelId 分开记,不会串到别的模型上
        if (!string.Equals(previousModelPath, config.ModelPath, StringComparison.Ordinal))
        {
            config.ObservedThinkingFields.Clear();
            config.ReasoningEveryAssistantModelIds.Clear();
        }
        config.ModelId = ModelId.Trim();
        config.ModelDescription = ModelDescription;
        config.ContextLength =
            int.TryParse(ContextLengthText.Trim(), out var contextLength) && contextLength > 0 ? contextLength : 0;
        config.MaxTokens =
            int.TryParse(MaxTokensText.Trim(), out var maxTokens) && maxTokens > 0 ? maxTokens : 0;
        config.ThinkingEffort = _thinkingEffortValue;
        // 没动过就保持"跟随推导"：把界面上那个派生值当成用户选择存下来，
        // 会让下次模型表修好之后仍然沿用旧的判断（<see cref="BaseRemoteModelConfig.IsVisionOverride"/>）
        bool derivedVision = ModelCapabilityResolver.ResolveVision(config);
        config.IsVisionOverride = IsVision == derivedVision ? null : IsVision;
        config.OmitSamplingParams = OmitSamplingParams;
        config.ReplayExistingReasoningOverride = ReplayExistingReasoningOverride;
        // 格式从地址尾段反推：界面上选的是完整地址，两者不可能打架（ADR 0102）。
        // 认不出尾段（只填到 /v1 或裸域）时保留原值——运行时按格式补后缀，那种地址本来就合法
        config.ApiFormat = RemoteEndpoints.FormatOf(config.ModelPath) ?? config.ApiFormat;
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
        // 目录驱动型服务商（如 CommandCode）无预设变体表，候选全在运行时目录里：
        // ApplyConfigTraits 已把目录候选清空重建，这里必须像切服务商一样把目录候选拉回来，
        // 否则 HasModelIdOptions 变 false、模型 ID 下拉框直接消失（有缓存不重复拉网络）
        TryAutoLoadCatalog();
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
            _suppressModelPathDirty = true;
            ModelPath = config.ModelPath ?? "";
            _suppressModelPathDirty = false;
            _modelPathEditedByUser = false; //回填不是用户改的
            ModelId = config.ModelId ?? "";
            ModelDescription = config.ModelDescription ?? "";
            ContextLengthText = config.ContextLength > 0 ? config.ContextLength.ToString() : "";
            MaxTokensText = config.MaxTokens > 0 ? config.MaxTokens.ToString() : "";
            _thinkingEffortValue = ThinkingEfforts.Normalize(config.ThinkingEffort);
            IsVision = config.IsVision;
            OmitSamplingParams = config.OmitSamplingParams;
            ReplayExistingReasoningOverride = config.ReplayExistingReasoningOverride;
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
        IsPresetProvider = config.GetType() != typeof(RemoteModelConfig);
        ModelIdOptions.Clear();
        foreach (string option in config.ModelIdVariants.Keys)
        {
            bool isVision = false;
            int contextLength = 0;
            int maxTokens = 0;
            bool? replayExistingReasoning = null;
            bool omitSamplingParams = false;
            string alias = "";
            if (config.ModelIdVariants.TryGetValue(option, out var variant))
            {
                isVision = variant.IsVision;
                contextLength = variant.ContextLength;
                maxTokens = variant.MaxTokens;
                replayExistingReasoning = variant.ReplayExistingReasoning;
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
                ReplayExistingReasoning = replayExistingReasoning,
                OmitSamplingParams = omitSamplingParams,
                Price = ResolvePrice(option),
                Efforts = ResolveEfforts(option),
            });
        }

        HasModelIdOptions = ModelIdOptions.Count > 0;

        // 让下拉框定位到当前 ModelId;创建/复制模式按预设预填默认上下文与视觉。
        // 重建期间被回写冲掉的文本在这里补回去(见方法头的注释)。
        _suppressModelIdPrefill = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x.Id == currentId);
        ModelId = currentId;
        _suppressModelIdPrefill = false;

        // ComboBox 绑的是 VisibleModelIdOptions:重建候选后必须同步刷新可见列表,
        // 否则预设候选(无目录的服务商)或复制后下拉会一直空(与 ApplyCatalog 同口径)
        RefreshVisibleModelIdOptions();
        RefreshEndpointOptions(applyRecommendation: false); //只重建地址候选，不动已填的地址
        if (!IsEditMode) ApplyModelIdPreset(SelectedModelIdOption);
        RefreshThinkingOptions();
    }

    /// <summary>
    /// 下拉选择模型时:同步 ModelId,并按预设默认值预填上下文与视觉状态
    /// </summary>
    /// <param name="value">选中的模型条目</param>
    partial void OnSelectedModelIdOptionChanged(ModelIdOptionItem? value)
    {
        if (value == null || _suppressModelIdPrefill) return;
        _modelPathEditedByUser = false; //选模型＝采用这个模型的端点推荐
        _suppressModelIdPrefill = true;
        ModelId = value.Id;
        _suppressModelIdPrefill = false;
        ApplyModelIdPreset(value);
        TrySyncModelName(value);
        RefreshThinkingOptions();
        RefreshEndpointOptions(applyRecommendation: true);
    }

    partial void OnSelectedThinkingOptionChanged(ThinkingOptionItem? value)
    {
        if (_suppressThinkingOptionWrite) return;
        _thinkingEffortValue = value?.Value ?? ThinkingEfforts.Auto;
    }

    /// <summary>
    /// 按当前模型声明重建思考档位候选。模型<b>声明了</b>取值集合、而已选值不在其中时退回「不干预」——
    /// 留着会让下次请求发出模型不认的档位（服务端 400 或静默降级）。
    /// 没声明的模型给的是兜底阶梯，那只是猜测，此时保留用户已选的值。
    /// </summary>
    private void RefreshThinkingOptions()
    {
        string desired = ThinkingEfforts.Normalize(_thinkingEffortValue);
        ThinkingProfile profile = EffectiveConfigType is { } configType
            ? ModelCapabilityResolver.ResolveThinkingProfile(configType, ModelId)
            : new ThinkingProfile(ThinkingEfforts.Fallback, false);

        _suppressThinkingOptionWrite = true;
        try
        {
            ThinkingOptions.Clear();
            ThinkingOptions.Add(new ThinkingOptionItem(ThinkingEfforts.Auto));
            ThinkingOptions.Add(new ThinkingOptionItem(ThinkingEfforts.Off));
            // 声明里可能就有 none（23 条模型这么写），与「关闭」是同一个语义，不能重复成两条
            foreach (string effort in profile.Levels)
            {
                if (ThinkingOptions.All(option => option.Value != effort))
                    ThinkingOptions.Add(new ThinkingOptionItem(effort));
            }

            if (!profile.Declared && desired.Length > 0 &&
                ThinkingOptions.All(option => option.Value != desired))
            {
                ThinkingOptions.Add(new ThinkingOptionItem(desired));
            }

            ThinkingOptionItem selected = ThinkingOptions.FirstOrDefault(option => option.Value == desired) ??
                                          ThinkingOptions[0];
            SelectedThinkingOption = selected;
            _thinkingEffortValue = selected.Value;
        }
        finally
        {
            _suppressThinkingOptionWrite = false;
        }
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
    /// 把目录条目灌进候选列表：显示我们支持的对话端点那条线上的模型，
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
            // 只有显式声明且不含任何我们支持的对话端点才过滤（ADR 0092）。
            // 三条线都算：/chat/completions、/responses、/messages（Anthropic）。
            // 早期只支持 chat 线时 /messages-only 的 Claude 系会被滤掉——那条线接上后不再滤
            if (entry.SupportedEndpoints.Count > 0 && !entry.SupportsEndpoint("/chat/completions")
                && !entry.SupportsEndpoint("/responses") && !entry.SupportsEndpoint("/messages")) continue;
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
                ReplayExistingReasoning = preset?.ReplayExistingReasoning,
                ApiFormats = entry.SupportedApiFormats(),
                Price = ResolvePrice(entry.Id),
                Efforts = ResolveEfforts(entry.Id),
            });
        }

        HasModelIdOptions = ModelIdOptions.Count > 0;
        _suppressModelIdPrefill = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x.Id == currentId);
        ModelId = currentId;
        _suppressModelIdPrefill = false;
        RefreshVisibleModelIdOptions();
        RefreshEndpointOptions(applyRecommendation: false); //目录只是刷新候选，已填的地址不重写
        if (!IsEditMode) ApplyModelIdPreset(SelectedModelIdOption);
        RefreshThinkingOptions();
    }

    /// <summary>按 ModelId 文本过滤可见候选（id 或显示名，忽略大小写子串；空文本=全量），再按选定维度排序</summary>
    private void RefreshVisibleModelIdOptions()
    {
        string filter = ModelId.Trim();
        List<ModelIdOptionItem> matches = ModelIdOptions
            .Where(option => filter.Length == 0 ||
                             option.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                             option.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        EModelPriceSort sort = SelectedSortOption?.Sort ?? EModelPriceSort.Default;
        if (sort != EModelPriceSort.Default)
        {
            // 没公布价格的排最后（不能当 0 比，否则"未公布"会挤在免费模型前面）；
            // 同价再按 ID 排，免得每次刷新顺序跟着字典遍历抖
            matches = matches
                .OrderBy(option => option.SortKey(sort) is null ? 1 : 0)
                .ThenBy(option => option.SortKey(sort) ?? 0m)
                .ThenBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        VisibleModelIdOptions.Clear();
        foreach (ModelIdOptionItem option in matches) VisibleModelIdOptions.Add(option);

        // 按全部候选判（不是过滤后的）：搜到只剩无价的一条时排序头不该突然消失
        HasSortablePrices = ModelIdOptions.Any(option => option.Price is { IsKnown: true });
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
        RefreshThinkingOptions();
        // 只有命中了候选（输完整个 ID）才按它的推荐走：逐字符都改地址等于边打字边抢方向
        RefreshEndpointOptions(applyRecommendation: SelectedModelIdOption != null);
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
        IsVision = option.IsVision;
        OmitSamplingParams = option.OmitSamplingParams;
        // 换了模型,之前针对旧模型的手动覆盖不该带过来——重置为"跟随预设",预设值随下拉框联动展示
        ReplayExistingReasoningOverride = null;
    }

    /// <summary>编辑模式认原实例的配置类型，创建/复制模式认草稿（价格与档位都按这个去查表）</summary>
    private Type? EffectiveConfigType => _sourceInfo?.Config.GetType() ?? _draftConfig?.GetType();

    /// <summary>当前表单对应的配置实例：编辑模式是原实例，创建/复制模式是草稿</summary>
    private BaseRemoteModelConfig? CurrentConfig => _sourceInfo?.Config ?? _draftConfig;

    /// <summary>
    /// 当前模型的挂牌单价；供应商没公布、或自定义服务商查不到时为 null
    /// </summary>
    private ModelPrice? ResolvePrice(string modelId) =>
        EffectiveConfigType is { } configType &&
        ModelCapabilityResolver.TryGetPrice(configType, modelId, out ModelPrice price)
            ? price
            : null;

    /// <summary>当前模型声明的思考档位；没声明为空串</summary>
    private string ResolveEfforts(string modelId) =>
        EffectiveConfigType is { } configType ? ModelCapabilityResolver.ResolveEfforts(configType, modelId) : "";
}
