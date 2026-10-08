using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Options;

/// <summary>
/// 一个本地语音模型的参数窗：照模型 spec 列出能调的参数，常用的在前、其余收在「更多」里。
/// 改了立刻生效（下次合成、识别就带上，试听即可听到），关窗时存盘
/// </summary>
public partial class AudioModelOptionsViewData : ObservableObject
{
    private readonly AudioCppSettingConfig _settings;
    private readonly Func<Task>? _preview;

    [ObservableProperty] private bool _showMore;

    /// <param name="model">哪个模型（名字、试听状态）</param>
    /// <param name="options">它能调的参数</param>
    /// <param name="settings">存改过的值</param>
    /// <param name="preview">试听；识别模型为 null</param>
    public AudioModelOptionsViewData(AudioModelItemData model, IReadOnlyList<AudioCppOption> options,
        AudioCppSettingConfig settings, Func<Task>? preview)
    {
        Model = model;
        _settings = settings;
        _preview = preview;
        Dictionary<string, string> saved = settings.ModelOptions.GetValueOrDefault(model.Name) ?? new();
        List<AudioOptionItemData> items = options
            .Select(x => AudioOptionItemData.Create(x, saved.GetValueOrDefault(x.Name)))
            .ToList();
        foreach (AudioOptionItemData item in items) item.Changed += Apply;
        Common = items.Where(x => AudioCppTunableOptions.IsCommon(x.Name)).ToList();
        More = items.Where(x => !AudioCppTunableOptions.IsCommon(x.Name)).ToList();
        // 没有常用参数时直接摊开
        _showMore = Common.Count == 0;
    }

    /// <summary>
    /// 哪个模型
    /// </summary>
    public AudioModelItemData Model { get; }

    /// <summary>
    /// 常用参数（中文名与说明）
    /// </summary>
    public IReadOnlyList<AudioOptionItemData> Common { get; }

    /// <summary>
    /// 其余参数（参数名与上游英文说明）
    /// </summary>
    public IReadOnlyList<AudioOptionItemData> More { get; }

    public bool HasCommon => Common.Count > 0;

    /// <summary>
    /// 有其余参数、且还收着
    /// </summary>
    public bool CanShowMore => More.Count > 0 && !ShowMore;

    /// <summary>
    /// 「更多参数（N）」
    /// </summary>
    public string MoreText => Loc.Text(LangKey.AudioOptionsMore, More.Count);

    public bool CanPreview => _preview != null;

    /// <summary>
    /// 有改过的（「全部还原」可点）
    /// </summary>
    public bool HasChanges => Common.Concat(More).Any(x => x.IsChanged);

    /// <summary>
    /// 存盘（关窗时）
    /// </summary>
    public void Save() => _settings.Save();

    partial void OnShowMoreChanged(bool value) => OnPropertyChanged(nameof(CanShowMore));

    [RelayCommand]
    private void ExpandMore() => ShowMore = true;

    [RelayCommand]
    private void ResetAll()
    {
        foreach (AudioOptionItemData item in Common.Concat(More).Where(x => x.IsChanged))
            item.ResetCommand.Execute(null);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Preview() => _preview?.Invoke() ?? Task.CompletedTask;

    // 整份换掉而不是就地改：合成在后台线程读它
    private void Apply()
    {
        Dictionary<string, string> changed = Common.Concat(More).Where(x => x.Value != null)
            .ToDictionary(x => x.Name, x => x.Value!, StringComparer.Ordinal);
        if (changed.Count == 0) _settings.ModelOptions.Remove(Model.Name);
        else _settings.ModelOptions[Model.Name] = changed;
        Model.IsTuned = changed.Count > 0;
        OnPropertyChanged(nameof(HasChanges));
    }
}
