using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Options;

/// <summary>
/// 整数、小数：数字框，留空不发
/// </summary>
public sealed class NumberOptionData : AudioOptionItemData
{
    private const double DecimalLimit = 1e15; //spec 偶有极大值，超出 decimal 就当没给

    private decimal? _number;

    /// <param name="option">spec 声明的参数</param>
    /// <param name="value">改过的值；null 为没改</param>
    public NumberOptionData(AudioCppOption option, string? value) : base(option, value)
    {
        _number = Parse(value);
    }

    /// <summary>
    /// 数字框里的数；空为不发、由模型用自己的默认
    /// </summary>
    public decimal? Number
    {
        get => _number;
        set
        {
            if (!SetProperty(ref _number, value)) return;
            SetValue(value is { } number ? Format(number) : null);
        }
    }

    public decimal Minimum => Option.Min is { } min && Math.Abs(min) < DecimalLimit ? (decimal)min : decimal.MinValue;

    public decimal Maximum => Option.Max is { } max && Math.Abs(max) < DecimalLimit ? (decimal)max : decimal.MaxValue;

    /// <summary>
    /// 点一下增减多少：整数 1；小数按范围或默认值的量级
    /// </summary>
    public decimal Increment
    {
        get
        {
            if (Option.Type == EAudioCppOptionType.Integer) return 1;
            double scale = Option is { Min: { } min, Max: { } max } ? max - min
                : Math.Abs(Parse(Option.Default) is { } fallback ? (double)fallback : 1);
            return scale <= 1 ? 0.05m : scale <= 10 ? 0.1m : 1m;
        }
    }

    public string FormatString => Option.Type == EAudioCppOptionType.Integer ? "0" : "0.###";

    /// <summary>
    /// 空着时的提示：spec 写的默认值，没写就「模型默认」
    /// </summary>
    public string Watermark => Option.Default is { Length: > 0 } text
        ? Loc.Text(LangKey.AudioOptionDefaultIs, text)
        : Loc.Text(LangKey.AudioOptionModelDefault);

    protected override void OnReset()
    {
        _number = null;
        OnPropertyChanged(nameof(Number));
    }

    private string Format(decimal number) => Option.Type == EAudioCppOptionType.Integer
        ? decimal.Round(number).ToString("0", CultureInfo.InvariantCulture)
        : number.ToString("0.##########", CultureInfo.InvariantCulture);

    private static decimal? Parse(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;
}

/// <summary>
/// 开关
/// </summary>
public sealed class ToggleOptionData : AudioOptionItemData
{
    /// <param name="option">spec 声明的参数</param>
    /// <param name="value">改过的值；null 为没改</param>
    public ToggleOptionData(AudioCppOption option, string? value) : base(option, value)
    {
    }

    public bool IsOn
    {
        get => (Value ?? Option.Default) == "true";
        set
        {
            SetValue(value ? "true" : "false");
            OnPropertyChanged();
        }
    }

    protected override void OnReset() => OnPropertyChanged(nameof(IsOn));
}

/// <summary>
/// 下拉里的一项
/// </summary>
/// <param name="Value">值；null 为「默认」那项</param>
/// <param name="DisplayName">显示名</param>
public sealed record AudioOptionChoice(string? Value, string DisplayName);

/// <summary>
/// 几选一：下拉，第一项是默认
/// </summary>
public sealed class ChoiceOptionData : AudioOptionItemData
{
    private AudioOptionChoice _selected;

    /// <param name="option">spec 声明的参数</param>
    /// <param name="value">改过的值；null 为没改</param>
    public ChoiceOptionData(AudioCppOption option, string? value) : base(option, value)
    {
        string fallback = option.Default is { Length: > 0 } text
            ? Loc.Text(LangKey.AudioOptionChoiceDefault, text)
            : Loc.Text(LangKey.AudioOptionModelDefault);
        Choices = option.Values.Select(x => new AudioOptionChoice(x, x)).Prepend(new AudioOptionChoice(null, fallback))
            .ToList();
        _selected = Choices.FirstOrDefault(x => x.Value != null && x.Value == value) ?? Choices[0];
    }

    public IReadOnlyList<AudioOptionChoice> Choices { get; }

    public AudioOptionChoice Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            SetValue(value.Value);
        }
    }

    protected override void OnReset()
    {
        _selected = Choices[0];
        OnPropertyChanged(nameof(Selected));
    }
}

/// <summary>
/// 文字（含只给了上游预设名、列不出可选值的选项）
/// </summary>
public sealed class TextOptionData : AudioOptionItemData
{
    private string _text;

    /// <param name="option">spec 声明的参数</param>
    /// <param name="value">改过的值；null 为没改</param>
    public TextOptionData(AudioCppOption option, string? value) : base(option, value)
    {
        _text = value ?? "";
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value)) return;
            SetValue(value.Trim());
        }
    }

    /// <summary>
    /// 空着时的提示：默认值，没有就「模型默认」
    /// </summary>
    public string Watermark => Option.Default is { Length: > 0 } text
        ? Loc.Text(LangKey.AudioOptionChoiceDefault, text)
        : Loc.Text(LangKey.AudioOptionModelDefault);

    protected override void OnReset()
    {
        _text = "";
        OnPropertyChanged(nameof(Text));
    }
}
