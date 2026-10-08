using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Models.AudioModels.Voices;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 添加音色与音色库：抽卡、录音、导入三条路都落到「一段参考音频 + 文字 + 名字」，存进库；删音色时清掉各模型对它的选择
/// </summary>
public class VoiceCreateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-voice-create-{Guid.NewGuid():N}");
    private readonly VoiceLibrary _library;
    private readonly RecordingMessageService _messages = new();
    private readonly RecordingAudioPlayer _player = new();
    private readonly FakeAudioRecorder _recorder = new();
    private readonly List<(string Model, int Seed)> _draws = [];
    private readonly List<(string Model, string Voice)> _used = [];

    public VoiceCreateTests()
    {
        _library = new VoiceLibrary(Path.Combine(_directory, "voices"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task Draw_NewestFirst_PlaysIt_AndSavingMakesItTheModelsVoice()
    {
        VoiceCreateViewData form = Create();
        Assert.Equal(EVoiceSource.Draw, form.Source);

        await form.DrawCommand.ExecuteAsync(null);
        await form.DrawCommand.ExecuteAsync(null);

        Assert.Equal([2, 1], form.Draws.Select(x => x.Number));
        Assert.Same(form.Draws[0], form.SelectedDraw);
        Assert.Equal("ref 2", form.Transcript);
        Assert.Equal(2, _player.Played.Count); //每抽自动放一遍
        Assert.NotEqual(_draws[0].Seed, _draws[1].Seed);
        string autoName = form.Name;

        form.SelectedDraw = form.Draws[1];
        Assert.Equal("ref 1", form.Transcript);
        Assert.NotEqual(autoName, form.Name); //名字跟着选中的那一抽走

        VoiceSample saved = Assert.IsType<VoiceSample>(form.Save());
        Assert.Equal("ref 1", Assert.Single(_library.List()).Transcript);
        Assert.Equal(("vox", saved.Name), Assert.Single(_used));
    }

    [Fact]
    public void Record_AtReferenceRate_UsesPromptAsTranscript_RejectsSilence()
    {
        VoiceCreateViewData form = Create();
        form.SelectedSourceOption = form.Sources.Single(x => x.Source == EVoiceSource.Record);

        _recorder.Recording = FakeAudioRecorder.Speech(TimeSpan.FromSeconds(1), peak: 0);
        form.ToggleRecordingCommand.Execute(null);
        form.ToggleRecordingCommand.Execute(null);
        Assert.False(form.HasClip);
        Assert.Single(_messages.Notifications);

        _recorder.Recording = FakeAudioRecorder.Speech(TimeSpan.FromSeconds(6));
        form.ToggleRecordingCommand.Execute(null);
        Assert.Equal(VoiceCreateContext.SampleRate, _recorder.SampleRate);
        form.ToggleRecordingCommand.Execute(null);

        Assert.True(form.HasClip);
        Assert.Equal(form.RecordPrompt, form.Transcript);
        Assert.True(form.CanSave);
        Assert.NotNull(form.Save());
        Assert.Empty(_used); //录的不替任何模型做主
    }

    [Fact]
    public async Task Import_TranscribesWithLocalAsr_AndShortClipWarns()
    {
        VoiceCreateViewData form = Create(transcribe: true);
        form.SelectedSourceOption = form.Sources.Single(x => x.Source == EVoiceSource.Import);

        await form.ImportAsync("/music/小雨.mp3");

        Assert.Equal("heard 32044 bytes", form.Transcript);
        Assert.Equal("小雨.mp3", form.ImportedFileName);
        Assert.Equal("小雨", form.Name);
        Assert.NotEmpty(form.ClipWarning); //只有 1 秒
    }

    [Fact]
    public async Task TakenName_BlocksSaving()
    {
        _library.Save("小雨", [1], "x");
        VoiceCreateViewData form = Create();
        await form.DrawCommand.ExecuteAsync(null);

        form.Name = "小雨";

        Assert.True(form.HasNameError);
        Assert.False(form.CanSave);
        Assert.Null(form.Save());
    }

    [Fact]
    public async Task DeletingAVoice_ResetsModelsThatChoseIt()
    {
        _library.Save("小雨", WavEncoder.Encode(new float[2400], 24000, 1), "x");
        AudioCppSettingConfig settings = new();
        settings.TtsVoices["vox"] = "小雨";
        settings.TtsVoices["pocket"] = "alba";
        VoiceLibraryViewData list = new(_library, settings, _messages, _player,
            () => Task.FromResult<VoiceSample?>(null), _ => { }, action => action());
        list.Refresh();

        await list.DeleteCommand.ExecuteAsync(list.Items.Single());

        Assert.Empty(list.Items);
        Assert.Equal(["pocket"], settings.TtsVoices.Keys);
    }

    private VoiceCreateViewData Create(bool transcribe = false)
    {
        int count = 0;
        VoiceCreateContext context = new(_library, _player, _recorder, _messages,
            [new VoiceDrawModel("vox", "VoxCPM")],
            (model, seed, _) =>
            {
                _draws.Add((model, seed));
                return Task.FromResult(new VoiceDraft(WavEncoder.Encode(new float[24000 * 6], 24000, 1), $"ref {++count}"));
            },
            transcribe ? (wav, _) => Task.FromResult($"heard {wav.Length} bytes") : null,
            _ => FakeAudioRecorder.Speech(TimeSpan.FromSeconds(1)),
            (model, voice) => _used.Add((model, voice)),
            action => action());
        return new VoiceCreateViewData(context);
    }
}
