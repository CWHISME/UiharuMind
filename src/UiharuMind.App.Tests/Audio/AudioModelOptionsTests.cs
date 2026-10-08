using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Models.AudioModels;
using UiharuMind.Features.Models.AudioModels.Options;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 模型参数窗：常用的在前、其余收起；改了立刻写进设置，填了就发（哪怕等于 spec 默认值），清空或还原才不存；
/// spec 已不认的旧值随下次改动丢掉
/// </summary>
public class AudioModelOptionsTests
{
    private readonly AudioCppSettingConfig _settings = new();

    internal static readonly AudioCppOption[] Options =
    [
        new("seed", EAudioCppOptionType.Integer, "Seed.", null, 0, null, []),
        new("temperature", EAudioCppOptionType.Number, "Temp.", "0.7", 0, 2, []),
        new("text_chunk_mode", EAudioCppOptionType.Choice, "Chunking.", "tag_aware", null, null, ["default", "tag_aware"]),
        new("retry_badcase", EAudioCppOptionType.Toggle, "Retry.", "true", null, null, []),
        new("template", EAudioCppOptionType.Choice, "Preset only.", "full", null, null, [])
    ];

    [Fact]
    public void Edits_WriteThrough_DefaultsAndResetsAreNotStored()
    {
        AudioModelOptionsViewData options = Open();
        Assert.Equal(["seed", "temperature"], options.Common.Select(x => x.Name));
        Assert.Equal(["text_chunk_mode", "retry_badcase", "template"], options.More.Select(x => x.Name));
        Assert.True(options.CanShowMore);
        Assert.IsType<TextOptionData>(options.More[2]); //只给了预设名的选项列不出值，当文字填

        NumberOptionData seed = (NumberOptionData)options.Common[0];
        NumberOptionData temperature = (NumberOptionData)options.Common[1];
        Assert.Null(seed.Number);
        Assert.Null(temperature.Number); //没填就不发，spec 的默认值只作提示
        Assert.Contains("0.7", temperature.Watermark);
        Assert.Equal(0.1m, temperature.Increment);

        seed.Number = 42.0m;
        temperature.Number = 0.3m;
        ((ToggleOptionData)options.More[1]).IsOn = false;
        ChoiceOptionData chunk = (ChoiceOptionData)options.More[0];
        chunk.Selected = chunk.Choices.Single(x => x.Value == "default");
        Assert.Equal(new Dictionary<string, string>
        {
            ["seed"] = "42", ["temperature"] = "0.3", ["retry_badcase"] = "false", ["text_chunk_mode"] = "default"
        }, _settings.ModelOptions["vox"]);
        Assert.True(options.Model.IsTuned);

        temperature.Number = 0.70m; //等于 spec 默认值也照发：上游写的不一定是模型实际用的
        Assert.Equal("0.7", _settings.ModelOptions["vox"]["temperature"]);
        temperature.Number = null;
        chunk.ResetCommand.Execute(null);
        Assert.Same(chunk.Choices[0], chunk.Selected);
        Assert.Equal(["retry_badcase", "seed"], _settings.ModelOptions["vox"].Keys.Order());

        options.ResetAllCommand.Execute(null);
        Assert.False(_settings.ModelOptions.ContainsKey("vox"));
        Assert.False(options.Model.IsTuned);
        Assert.False(options.HasChanges);
        Assert.Null(seed.Number);
    }

    [Fact]
    public void SavedValues_LoadBack_StaleOnesDropOnNextEdit()
    {
        _settings.ModelOptions["vox"] = new() { ["temperature"] = "1.2", ["renamed_upstream"] = "1" };

        AudioModelOptionsViewData options = Open();
        Assert.True(options.Model.IsTuned);
        Assert.Equal(1.2m, ((NumberOptionData)options.Common[1]).Number);
        Assert.True(options.Common[1].IsChanged);

        ((ToggleOptionData)options.More[1]).IsOn = false;
        Assert.Equal(["retry_badcase", "temperature"], _settings.ModelOptions["vox"].Keys.Order());
    }

    private AudioModelOptionsViewData Open()
    {
        AudioModelItemData model = new(new AudioCppModelEntry("vox", "voxcpm1", "/m/v.gguf", "tts"), null, 0, _settings);
        return new AudioModelOptionsViewData(model, Options, _settings, null);
    }
}
