using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Features.Models.AudioModels.Options;

/// <summary>
/// 参数窗里的一项：按值类型分子类（数值、开关、选项、文字），各自一个模板。
/// 值统一存成字符串（invariant 格式），null 为没改、不发给模型
/// </summary>
public abstract partial class AudioOptionItemData : ObservableObject
{
    private string? _value;

    /// <param name="option">spec 声明的参数</param>
    /// <param name="value">改过的值；null 为没改</param>
    protected AudioOptionItemData(AudioCppOption option, string? value)
    {
        Option = option;
        _value = value;
        AudioOptionText text = AudioOptionText.For(option);
        Label = text.Label;
        Note = text.Note;
    }

    /// <summary>
    /// spec 声明的参数
    /// </summary>
    public AudioCppOption Option { get; }

    /// <summary>
    /// 参数名（原样）
    /// </summary>
    public string Name => Option.Name;

    /// <summary>
    /// 显示名：常用参数是中文名，其余是参数名
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// 说明：常用参数是中文说明，其余是上游英文原文
    /// </summary>
    public string Note { get; }

    /// <summary>
    /// 显示名与参数名不是一回事时（常用参数的中文名），旁边再标上参数名
    /// </summary>
    public bool ShowsName => !string.Equals(Label.Replace(' ', '_'), Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 改过的值；null 为没改
    /// </summary>
    public string? Value => _value;

    /// <summary>
    /// 改过（显示「还原」）
    /// </summary>
    public bool IsChanged => _value != null;

    /// <summary>
    /// 值变了（参数窗据此写回设置）
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 按值类型建一项
    /// </summary>
    /// <param name="option">spec 声明的参数</param>
    /// <param name="value">改过的值；null 为没改</param>
    /// <returns>对应的子类</returns>
    public static AudioOptionItemData Create(AudioCppOption option, string? value) => option.Type switch
    {
        EAudioCppOptionType.Integer or EAudioCppOptionType.Number => new NumberOptionData(option, value),
        EAudioCppOptionType.Toggle => new ToggleOptionData(option, value),
        EAudioCppOptionType.Choice when option.Values.Count > 0 => new ChoiceOptionData(option, value),
        _ => new TextOptionData(option, value)
    };

    /// <summary>
    /// 改值；为空当没改。与 spec 默认值相同也照发：上游写的默认不一定是模型实际用的（VoxCPM1 写 50 步、实际 10 步）
    /// </summary>
    /// <param name="value">新值；null 为还原</param>
    protected void SetValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) value = null;
        if (value == _value) return;
        _value = value;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsChanged));
        Changed?.Invoke();
    }

    /// <summary>
    /// 还原后把控件上的值刷回默认
    /// </summary>
    protected abstract void OnReset();

    [RelayCommand]
    private void Reset()
    {
        SetValue(null);
        OnReset();
    }
}
