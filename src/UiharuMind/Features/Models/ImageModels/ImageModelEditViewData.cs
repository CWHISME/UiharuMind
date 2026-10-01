using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Models;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 服务商预设的一项
/// </summary>
/// <param name="Preset">预设</param>
/// <param name="DisplayName">显示名（自定义那项本地化，品牌名原样）</param>
public sealed record ImageModelPresetItem(ImageModelPreset Preset, string DisplayName);

/// <summary>
/// 分辨率档的一项
/// </summary>
/// <param name="Value">档位</param>
/// <param name="Label">形如 <c>2K</c></param>
public sealed record ImageResolutionOption(EImageResolution Value, string Label);

/// <summary>
/// 生图模型编辑表单。纯逻辑：开窗口、跳官网这些调系统的事归窗口后台代码。
/// 确认时产出一个新实例，由列表整项替换——不就地改活实例，取消就是把表单丢掉。
/// </summary>
public partial class ImageModelEditViewData : ObservableObject
{
    private const int MinTimeoutSeconds = 10;
    private const int MaxTimeoutSeconds = 3600;

    private readonly IReadOnlyCollection<string> _takenNames;
    private readonly IReadOnlyList<RemoteModelInfo> _chatModels;
    private string _autoName = string.Empty; //随模型 id 自动填的名字;用户改过就不再跟
    private bool _syncingModelId; //模型 id 文本与下拉选中项互相对齐期间

    [ObservableProperty] private ImageModelPresetItem? _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameError), nameof(CanConfirm))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEndpointError), nameof(CanConfirm), nameof(CopySources), nameof(HasCopySources))]
    private string _endpoint = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModelIdError), nameof(CanConfirm))]
    private string _modelId = string.Empty;

    /// <summary>
    /// 下拉框选中的候选。可编辑下拉框的 <c>Text</c> 与 <c>SelectedItem</c> 是两条双向绑定，
    /// 两边一旦不一致，控件回写就会把手输的文本冲掉——所以与 <see cref="ModelId"/> 始终保持一致
    /// </summary>
    [ObservableProperty] private string? _selectedModelIdOption;

    /// <summary>有模型 id 候选（自定义没有，退回普通输入框）</summary>
    [ObservableProperty] private bool _hasModelIdOptions;

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private EImageDialect _dialect = EImageDialect.OpenAI;
    [ObservableProperty] private ImageResolutionOption _resolution;
    [ObservableProperty] private bool _supportsEditing = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimeoutError), nameof(CanConfirm))]
    private string _timeoutText = "360";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtraBodyError), nameof(CanConfirm))]
    private string _extraBody = string.Empty;

    /// <param name="source">要编辑的模型；null 为新建</param>
    /// <param name="takenNames">其他生图模型已用的名字</param>
    /// <param name="chatModels">已有的远程对话模型，供沿用密钥</param>
    public ImageModelEditViewData(ImageModelInfo? source, IReadOnlyCollection<string> takenNames,
        IReadOnlyList<RemoteModelInfo> chatModels)
    {
        _takenNames = takenNames;
        _chatModels = chatModels;
        _resolution = ResolutionOptions.First(o => o.Value == EImageResolution.Res2K);
        Presets = ImageModelPreset.All
            .Select(p => new ImageModelPresetItem(p,
                p.Key == ImageModelPreset.CustomKey ? Loc.Text(LangKey.ImageModelPresetCustom) : p.DisplayName))
            .ToList();
        IsEditMode = source != null;

        if (source == null)
        {
            SelectedPreset = Presets[0];
            return;
        }

        Name = source.Name;
        Endpoint = source.Endpoint;
        ModelId = source.ModelId;
        ApiKey = source.ApiKey;
        Dialect = source.Dialect;
        Resolution = ResolutionOptions.First(o => o.Value == source.Resolution);
        SupportsEditing = source.SupportsEditing;
        TimeoutText = source.TimeoutSeconds.ToString();
        ExtraBody = source.ExtraBody;
        // 编辑时只借预设的官网与模型 id 候选,不回填任何字段
        _selectedPreset = Presets.FirstOrDefault(p => p.Preset.Dialect == source.Dialect &&
                                                      SameHost(p.Preset.Endpoint, source.Endpoint));
        RebuildModelIdOptions();
    }

    /// <summary>编辑已有模型（服务商不可换）</summary>
    public bool IsEditMode { get; }

    /// <summary>服务商预设</summary>
    public IReadOnlyList<ImageModelPresetItem> Presets { get; }

    /// <summary>接口格式候选</summary>
    public IReadOnlyList<EImageDialect> DialectOptions { get; } = Enum.GetValues<EImageDialect>();

    /// <summary>分辨率档候选</summary>
    public IReadOnlyList<ImageResolutionOption> ResolutionOptions { get; } = Enum.GetValues<EImageResolution>()
        .Select(r => new ImageResolutionOption(r, r.ToTierLabel()))
        .ToList();

    /// <summary>当前预设给的模型 id 候选。集合实例不换，换预设时就地重建</summary>
    public ObservableCollection<string> ModelIdOptions { get; } = new();

    /// <summary>当前预设的开放平台地址；没有为空串</summary>
    public string WebsiteUrl => SelectedPreset?.Preset.WebsiteUrl ?? string.Empty;

    /// <summary>有官网可跳</summary>
    public bool HasWebsite => WebsiteUrl.Length > 0;

    /// <summary>同一服务地址下、填了密钥的对话模型：一键沿用它的密钥</summary>
    public IReadOnlyList<RemoteModelInfo> CopySources => _chatModels
        .Where(m => !string.IsNullOrEmpty(m.ApiKey) && SameHost(m.ModelPath, Endpoint))
        .ToList();

    /// <summary>有可沿用的密钥</summary>
    public bool HasCopySources => CopySources.Count > 0;

    /// <summary>名字为空或与其他生图模型重名</summary>
    public bool HasNameError => string.IsNullOrWhiteSpace(Name) ||
                                _takenNames.Contains(Name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>地址不是合法的 http(s) 绝对地址</summary>
    public bool HasEndpointError => !Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out Uri? uri) ||
                                    uri.Scheme is not ("http" or "https");

    /// <summary>模型 id 为空</summary>
    public bool HasModelIdError => string.IsNullOrWhiteSpace(ModelId);

    /// <summary>超时不是范围内的整数</summary>
    public bool HasTimeoutError => ParseTimeout() == null;

    /// <summary>私有参数不是 JSON 对象</summary>
    public bool HasExtraBodyError => !new ImageModelInfo { ExtraBody = ExtraBody }.TryParseExtraBody(out _, out _);

    /// <summary>表单可以提交</summary>
    public bool CanConfirm => !HasNameError && !HasEndpointError && !HasModelIdError && !HasTimeoutError &&
                              !HasExtraBodyError;

    /// <summary>
    /// 按表单产出模型
    /// </summary>
    /// <returns>新实例</returns>
    public ImageModelInfo BuildResult()
    {
        return new ImageModelInfo
        {
            Name = Name.Trim(),
            Dialect = Dialect,
            Endpoint = Endpoint.Trim(),
            ModelId = ModelId.Trim(),
            ApiKey = ApiKey.Trim(),
            Resolution = Resolution.Value,
            SupportsEditing = SupportsEditing,
            TimeoutSeconds = ParseTimeout() ?? 360,
            ExtraBody = ExtraBody.Trim(),
        };
    }

    [RelayCommand]
    private void CopyKeyFrom(RemoteModelInfo source) => ApiKey = source.ApiKey;

    partial void OnSelectedPresetChanged(ImageModelPresetItem? value)
    {
        RebuildModelIdOptions();
        OnPropertyChanged(nameof(WebsiteUrl));
        OnPropertyChanged(nameof(HasWebsite));
        if (IsEditMode || value == null) return;

        ImageModelPreset preset = value.Preset;
        Endpoint = preset.Endpoint;
        Dialect = preset.Dialect;
        SupportsEditing = preset.SupportsEditing;
        ExtraBody = preset.ExtraBody;
        ModelId = preset.ModelIds.Count > 0 ? preset.ModelIds[0] : string.Empty;
    }

    partial void OnSelectedModelIdOptionChanged(string? value)
    {
        if (value == null || _syncingModelId) return;
        ModelId = value;
    }

    partial void OnModelIdChanged(string value)
    {
        if (_syncingModelId) return;
        _syncingModelId = true;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x == value);
        _syncingModelId = false;

        if (IsEditMode || (Name.Length > 0 && Name != _autoName)) return;
        _autoName = value.Trim();
        Name = _autoName;
    }

    // Clear 会让可编辑下拉框把 Text 置空、经双向绑定回写过来(文本模型窗口踩过):先留底,重建完补回
    private void RebuildModelIdOptions()
    {
        string keep = ModelId;
        _syncingModelId = true;
        ModelIdOptions.Clear();
        foreach (string id in SelectedPreset?.Preset.ModelIds ?? []) ModelIdOptions.Add(id);
        HasModelIdOptions = ModelIdOptions.Count > 0;
        ModelId = keep;
        SelectedModelIdOption = ModelIdOptions.FirstOrDefault(x => x == keep);
        _syncingModelId = false;
    }

    private int? ParseTimeout() =>
        int.TryParse(TimeoutText.Trim(), out int seconds) && seconds is >= MinTimeoutSeconds and <= MaxTimeoutSeconds
            ? seconds
            : null;

    private static bool SameHost(string left, string right) =>
        Uri.TryCreate(left.Trim(), UriKind.Absolute, out Uri? a) &&
        Uri.TryCreate(right.Trim(), UriKind.Absolute, out Uri? b) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
}
