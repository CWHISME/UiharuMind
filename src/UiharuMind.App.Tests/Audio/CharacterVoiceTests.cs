using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Characters.Voice;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 角色编辑里的朗读声音：按存的回填、换「用哪个」清掉旧音色、每改一项写回草稿；
/// 存的那个没了占位显示不悄悄换；不听语气的给提示；在线服务的音色自动列、本地的点了才列；试听念表单现在的样子
/// </summary>
public class CharacterVoiceTests
{
    private static readonly CharacterVoiceSource Vox = new(AudioCppProvider.LocalId, "vox", "VoxCPM", "vox",
        ["mine", "draw-2"], false, true);

    private static readonly CharacterVoiceSource Edge = new("service:Edge", null, "Edge", "在线", [], false, false);

    private static readonly CharacterVoiceSource OpenAi = new("service:OpenAI", null, "OpenAI", "在线",
        ["alloy", "nova"], true, false);

    private readonly List<SpeechStyle?> _committed = [];
    private readonly List<CharacterVoiceSource> _listed = [];
    private readonly RecordingAudioPlayer _player = new();
    private readonly FakeAudioProvider _tts = new();

    [Fact]
    public void RestoresSavedVoice_ThenEveryChangeIsWrittenBack()
    {
        using CharacterVoiceViewData voice = Create(new SpeechStyle
        {
            Provider = AudioCppProvider.LocalId, Model = "vox", Voice = "mine", Speed = 0.9f
        });

        Assert.Same(Vox, voice.SelectedSource);
        Assert.Equal("mine", voice.Voice.Text);
        Assert.True(voice.HasOwnSpeed);
        Assert.Empty(_committed); //回填不算改
        Assert.Empty(_listed); //本地的不自动拉起引擎

        voice.SelectedSource = OpenAi;
        Assert.Equal("", voice.Voice.Text); //上一个的音色这个不认
        Assert.Equal(["alloy", "nova"], voice.Voice.Options);
        voice.Voice.Text = "nova";
        voice.HasOwnSpeed = false;
        voice.Instruction = "  softly ";

        Assert.Equal(new SpeechStyle { Provider = "service:OpenAI", Voice = "nova", Instruction = "softly" }, _committed[^1]);
        Assert.False(voice.IsInstructionIgnored);

        voice.SelectedSource = voice.Sources[0];
        Assert.False(voice.CanChooseVoice);
        Assert.Equal(new SpeechStyle { Instruction = "softly" }, _committed[^1]);
        voice.Instruction = "";
        Assert.Null(_committed[^1]); //什么都没设就是没设
    }

    [Fact]
    public async Task OnlineVoicesAreListed_AndToneWarningFollowsTheSource()
    {
        using CharacterVoiceViewData voice = Create(null);

        voice.SelectedSource = Edge;
        await WaitFor(() => voice.Voice.Options.Count > 0);
        Assert.Equal([Edge], _listed);
        voice.Instruction = "softly";
        Assert.True(voice.IsInstructionIgnored);

        voice.SelectedSource = Vox;
        await voice.LoadVoicesCommand.ExecuteAsync(null);
        Assert.Equal(["mine", "draw-2", "x-1"], voice.Voice.Options); //已知的在前
    }

    [Fact]
    public void MissingSource_IsShownAsIs()
    {
        using CharacterVoiceViewData voice = Create(new SpeechStyle { Provider = "service:Deleted", Voice = "nova" });

        Assert.Equal(5, voice.Sources.Count); //四个能选的 + 一个占位
        Assert.Same(voice.Sources[^1], voice.SelectedSource);
        Assert.Contains("service:Deleted", voice.SelectedSource.Name);
        Assert.Equal(new SpeechStyle { Provider = "service:Deleted", Voice = "nova" }, voice.Build()); //不碰就原样存回
    }

    [Fact]
    public async Task Preview_ReadsTheFormAsItIsNow()
    {
        List<string?> providers = [];
        ReadAloudService readAloud = new(id =>
        {
            providers.Add(id);
            return _tts;
        }, _player, new RecordingMessageService());
        using CharacterVoiceViewData voice = Create(null, readAloud);
        voice.SelectedSource = Vox;
        voice.Voice.Text = "draw-2";

        await voice.PreviewCommand.ExecuteAsync(null);

        Assert.Equal([AudioCppProvider.LocalId], providers);
        AudioSynthesisRequest request = Assert.Single(_tts.Requests);
        Assert.Equal(("vox", "draw-2", "开场白。"), (request.Model, request.Voice, request.Text));
    }

    private CharacterVoiceViewData Create(SpeechStyle? style, IReadAloudService? readAloud = null) =>
        new(style, _committed.Add, [CharacterVoiceSource.Default, Vox, Edge, OpenAi], (source, _) =>
            {
                _listed.Add(source);
                return Task.FromResult<IReadOnlyList<string>>(source.IsLocal ? ["mine", "x-1"] : ["zh-CN-XiaoxiaoNeural"]);
            },
            readAloud ?? new ReadAloudService(_ => _tts, _player, new RecordingMessageService()), () => "开场白。", 1f);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
