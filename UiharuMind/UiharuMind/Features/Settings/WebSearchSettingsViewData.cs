/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.Configs;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 联网搜索这一块的全部设置:API 凭据与搜索链的健康状况。
///
/// 独立成一块而不是塞进 <see cref="AgentSettingViewData"/>:凭据和链路状态是同一件事的两面
/// (填没填 key 直接决定引擎是"可用"还是"未配置"),而 agent 设置页本身已经背着常规项、
/// MCP、技能三摊事了。
///
/// 健康面板的意义在于:日志能回答"刚才那次搜索走了谁",这里回答"现在这条链上谁还活着"。
/// 少了它,引擎悄悄失效或正处在熔断冷却里,界面上一点痕迹都没有。
/// </summary>
public partial class WebSearchSettingsViewData : ObservableObject
{
    private readonly SettingsWriteBack _writeBack = new(() => AgentSettingConfig.Current.Save()); //写回闸门

    [ObservableProperty] private bool _isProbingAll;

    //================= 搜索凭据 =================
    [ObservableProperty] private string _firecrawlApiKey = string.Empty;
    [ObservableProperty] private string _tavilyApiKey = string.Empty;
    [ObservableProperty] private string _braveSearchApiKey = string.Empty;

    /// <summary>按兜底优先级排列的引擎</summary>
    public ObservableCollection<WebSearchProviderItem> Items { get; } = new();

    public WebSearchSettingsViewData()
    {
        // 回填走 backing field,不惊动生成的 OnXChanged——那三个 handler 会顺带跑 Refresh(),
        // 而构造末尾本来就要 Refresh 一次,没必要为三个 key 各读一遍链路状态
        using (_writeBack.BeginLoad())
        {
            AgentSettingConfig config = AgentSettingConfig.Current;
            _firecrawlApiKey = config.FirecrawlApiKey;
            _tavilyApiKey = config.TavilyApiKey;
            _braveSearchApiKey = config.BraveSearchApiKey;
        }

        Refresh();
    }

    //================= 凭据:变更即存,并立刻反映到链路状态 =================
    partial void OnFirecrawlApiKeyChanged(string value)
    {
        AgentSettingConfig.Current.FirecrawlApiKey = value;
        _writeBack.Save();
        Refresh(); //填不填 key 直接决定引擎是"可用"还是"未配置"
    }

    partial void OnTavilyApiKeyChanged(string value)
    {
        AgentSettingConfig.Current.TavilyApiKey = value;
        _writeBack.Save();
        Refresh();
    }

    partial void OnBraveSearchApiKeyChanged(string value)
    {
        AgentSettingConfig.Current.BraveSearchApiKey = value;
        _writeBack.Save();
        Refresh();
    }

    /// <summary>测试按钮上的字，测试期间换成进行时</summary>
    public string ProbeAllButtonText => LocalizationManager.Instance.GetString(
        IsProbingAll ? "AgentSettingSearchProbing" : "AgentSettingSearchProbe");

    partial void OnIsProbingAllChanged(bool value)
    {
        OnPropertyChanged(nameof(ProbeAllButtonText));
    }

    /// <summary>重新读一遍各引擎的当前状态(是否配好、是否在冷却、上次为什么不通)</summary>
    [RelayCommand]
    public void Refresh()
    {
        IReadOnlyList<WebProviderStatus> statuses = WebSearchDiagnostics.GetStatuses();

        // 引擎数量是编译期定死的，条目就地更新，免得刷新一次整列表闪一下
        for (int i = 0; i < statuses.Count; i++)
        {
            if (i < Items.Count) Items[i].Apply(statuses[i]);
            else Items.Add(new WebSearchProviderItem(statuses[i]));
        }

        while (Items.Count > statuses.Count) Items.RemoveAt(Items.Count - 1);
    }

    /// <summary>挨个真发一次搜索，看谁还答得上来</summary>
    [RelayCommand]
    private async Task ProbeAll()
    {
        await AsyncCommandScope.RunAsync(
            v =>
            {
                IsProbingAll = v;
                foreach (WebSearchProviderItem item in Items)
                {
                    if (v) item.BeginProbe();
                    else item.EndProbe();
                }

                if (!v) Refresh(); //实测过程中可能有引擎被别的调用熔断，顺手把状态也刷新一遍
            },
            async () =>
            {
                foreach (WebProviderProbe probe in await WebSearchDiagnostics.ProbeAllAsync())
                {
                    Find(probe.Name)?.Apply(probe);
                }
            },
            skipIf: IsProbingAll);
    }

    /// <summary>只测某一个。改完 key、或想确认某个引擎是不是真活着时，不必把全链都跑一遍</summary>
    [RelayCommand]
    private async Task ProbeOne(WebSearchProviderItem? item)
    {
        if (item == null) return;

        await AsyncCommandScope.RunAsync(
            v =>
            {
                if (v)
                {
                    item.BeginProbe();
                    return;
                }

                item.EndProbe();
                Refresh();
            },
            async () =>
            {
                WebProviderProbe? probe = await WebSearchDiagnostics.ProbeAsync(item.Name);
                if (probe != null) item.Apply(probe);
            },
            skipIf: item.IsProbing);
    }

    private WebSearchProviderItem? Find(string name)
    {
        return Items.FirstOrDefault(x => x.Name == name);
    }
}

/// <summary>健康面板里的一行</summary>
public partial class WebSearchProviderItem : ObservableObject
{
    /// <summary>失败原因显示上限，超出截断——设置页不是日志窗口</summary>
    private const int MessageMaxLength = 90;

    /// <summary>引擎名，同时是熔断记账的键</summary>
    public string Name { get; }

    /// <summary>兜底链上的次序</summary>
    public string Order { get; }

    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private string _probeText = string.Empty;
    [ObservableProperty] private bool _isProbing;

    /// <summary>状态圆点的取色键，值域见 Assets/Themes/CustomStatusStyle.axaml</summary>
    [ObservableProperty] private string _statusKey = "Idle";

    /// <summary>实测抓回来的原物，空则不展开</summary>
    [ObservableProperty] private string _preview = string.Empty;

    // 熔断记账里的历史失败与刚刚实测的失败分开存:混在一个字段里,
    // 实测完那次 Refresh 会拿旧的历史原因把刚测出来的结论盖掉
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Message))]
    private string _lastError = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Message))]
    private string _probeError = string.Empty;

    private bool _isNotConfigured;

    /// <summary>行内那句说明：刚测过就说实测结果，否则说熔断记下的上次失败</summary>
    public string Message => ProbeError.Length > 0 ? ProbeError : LastError;

    public WebSearchProviderItem(WebProviderStatus status)
    {
        Name = status.Name;
        Order = status.Order.ToString();
        Apply(status);
    }

    /// <summary>
    /// 套用最新状态
    /// </summary>
    /// <param name="status">引擎状态</param>
    public void Apply(WebProviderStatus status)
    {
        _isNotConfigured = status.State == EWebProviderState.NotConfigured;
        StatusKey = status.State switch
        {
            EWebProviderState.Ready => "Ready",
            EWebProviderState.NotConfigured => "Idle",
            _ => "Warning" //熔断中:样式表里没有这个 Tag,落到默认的琥珀色
        };

        string text = LocalizationManager.Instance.GetString($"AgentSettingSearchState{status.State}");
        StateText = status.State == EWebProviderState.Cooling
            ? $"{text} · {status.Cooldown.TotalSeconds:F0}s"
            : text;
        LastError = Truncate(status.LastError);
    }

    /// <summary>
    /// 套用一次实测结果
    /// </summary>
    /// <param name="probe">实测结果</param>
    public void Apply(WebProviderProbe probe)
    {
        //没填 key 的引擎压根没测,状态栏已经写着"未配置",再打个叉是重复且误导
        if (_isNotConfigured)
        {
            ProbeText = string.Empty;
            return;
        }

        ProbeText = probe.Ok ? $"✓ {probe.ResultCount} · {probe.ElapsedMs} ms" : "✗";
        Preview = probe.Preview ?? string.Empty;
        ProbeError = probe.Ok ? string.Empty : Truncate(probe.Detail);
    }

    /// <summary>实测开始，先把上一轮的结论清掉，免得看着像刚测出来的</summary>
    public void BeginProbe()
    {
        IsProbing = true;
        ProbeText = "…";
        ProbeError = string.Empty;
        Preview = string.Empty;
    }

    /// <summary>实测收尾</summary>
    public void EndProbe()
    {
        IsProbing = false;
        if (ProbeText == "…") ProbeText = string.Empty; //没等到结果(比如引擎不在链上),别一直挂着省略号
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= MessageMaxLength ? flat : $"{flat[..MessageMaxLength]}…";
    }
}
