using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Data;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 本地生图模型参数窗里的一个组件
/// </summary>
/// <param name="Flag">交给 sd-server 的参数名</param>
/// <param name="Path">文件</param>
/// <param name="Exists">在本机</param>
public sealed record LocalImageComponentLine(string Flag, string Path, bool Exists);

/// <summary>
/// 本地生图模型的参数窗：名字与出图参数，「恢复推荐」按所属家族取回推荐值。组件只读，换组件是另装一个
/// </summary>
public partial class LocalImageModelEditViewData : ObservableObject
{
    private readonly ImageModelInfo _source;
    private readonly IReadOnlyCollection<string> _takenNames;
    private readonly ImageCatalogFamily? _family;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNameError), nameof(CanConfirm))]
    private string _name;

    [ObservableProperty] private decimal _steps;
    [ObservableProperty] private decimal _cfgScale;
    [ObservableProperty] private SettingChoice<string?> _sampler;
    [ObservableProperty] private SettingChoice<string?> _scheduler;
    [ObservableProperty] private SettingChoice<int> _baseResolution;
    [ObservableProperty] private string _negativePrompt;
    [ObservableProperty] private bool _supportsEditing;

    /// <param name="source">要编辑的本地模型</param>
    /// <param name="family">它在可下载列表里的家族（「恢复推荐」按它取）；自定义组合为 null</param>
    /// <param name="takenNames">其他生图模型已用的名字</param>
    /// <param name="modelRoot">模型目录（看组件在不在）</param>
    public LocalImageModelEditViewData(ImageModelInfo source, ImageCatalogFamily? family,
        IReadOnlyCollection<string> takenNames, string modelRoot)
    {
        _source = source;
        _takenNames = takenNames;
        LocalImageModel local = source.Local!;
        _family = family;
        _name = source.Name;
        _negativePrompt = local.NegativePrompt;
        _supportsEditing = source.SupportsEditing;
        Origin = ImageTierText.Origin(family?.Name, local.Tier);
        Components = local.Components
            .Select(x => x.Resolve(modelRoot))
            .Zip(local.Components, (path, c) => new LocalImageComponentLine(c.Flag, path, File.Exists(path)))
            .ToList();
        SamplerOptions = Options(SdCppSamplers.Samplers);
        SchedulerOptions = Options(SdCppSamplers.Schedulers);
        _sampler = SamplerOptions[0];
        _scheduler = SchedulerOptions[0];
        _baseResolution = ResolutionOptions[2];
        Load(local);
    }

    /// <summary>
    /// 家族 · 档位
    /// </summary>
    public string Origin { get; }

    /// <summary>
    /// 组件
    /// </summary>
    public IReadOnlyList<LocalImageComponentLine> Components { get; }

    /// <summary>
    /// 采样器选项（第一项是交给 sd.cpp 的默认）
    /// </summary>
    public IReadOnlyList<SettingChoice<string?>> SamplerOptions { get; }

    /// <summary>
    /// 调度器选项
    /// </summary>
    public IReadOnlyList<SettingChoice<string?>> SchedulerOptions { get; }

    /// <summary>
    /// 基准边长选项
    /// </summary>
    public IReadOnlyList<SettingChoice<int>> ResolutionOptions { get; } =
        [new(512, "512"), new(768, "768"), new(1024, "1024"), new(1280, "1280"), new(1536, "1536")];

    /// <summary>
    /// 能恢复推荐（从可下载列表装的）
    /// </summary>
    public bool CanReset => _family != null;

    /// <summary>
    /// 名字空了或重了
    /// </summary>
    public bool HasNameError => string.IsNullOrWhiteSpace(Name) || _takenNames.Contains(Name.Trim());

    /// <summary>
    /// 可以保存
    /// </summary>
    public bool CanConfirm => !HasNameError;

    /// <summary>
    /// 按表单建新实例：原条目复制一份，只换表单里的几项
    /// </summary>
    /// <returns>新的生图模型</returns>
    public ImageModelInfo BuildResult()
    {
        LocalImageModel local = _source.Local!.Clone();
        local.BaseResolution = BaseResolution.Value;
        local.NegativePrompt = NegativePrompt.Trim();
        local.Steps = (int)Steps;
        local.CfgScale = (float)CfgScale;
        local.Sampler = Sampler.Value;
        local.Scheduler = Scheduler.Value;
        return new ImageModelInfo
        {
            Name = Name.Trim(),
            Dialect = EImageDialect.Local,
            SupportsEditing = SupportsEditing,
            Local = local
        };
    }

    [RelayCommand]
    private void ResetRecommended()
    {
        if (_family == null) return;
        LocalImageModel recommended = new();
        LocalImageModels.ApplyRecommended(recommended, _family);
        Load(recommended);
        NegativePrompt = recommended.NegativePrompt;
        SupportsEditing = _family.SupportsEditing;
    }

    private void Load(LocalImageModel local)
    {
        Steps = local.Steps;
        CfgScale = (decimal)local.CfgScale;
        Sampler = SamplerOptions.FirstOrDefault(x => x.Value == local.Sampler) ?? SamplerOptions[0];
        Scheduler = SchedulerOptions.FirstOrDefault(x => x.Value == local.Scheduler) ?? SchedulerOptions[0];
        BaseResolution = ResolutionOptions.FirstOrDefault(x => x.Value == local.BaseResolution) ?? ResolutionOptions[2];
    }

    private static IReadOnlyList<SettingChoice<string?>> Options(IReadOnlyList<string> names) =>
        [new(null, Loc.Text(LangKey.LocalImageDefaultOption)), ..names.Select(x => new SettingChoice<string?>(x, x))];
}
