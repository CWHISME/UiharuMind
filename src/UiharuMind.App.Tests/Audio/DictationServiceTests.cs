using SharpHook.Data;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.Core;
using UiharuMind.Features.Audio;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 听写：录完识别、把字送进去向；开录就预热识别模型；误触、静音、麦克风打不开、去向没了都有交代；
/// 按住说话只在去向可用时开录，短按当误触
/// </summary>
public class DictationServiceTests
{
    private readonly RecordingMessageService _messages = new();
    private readonly FakeAudioProvider _asr = new() { TranscribedText = "  你好世界 " };
    private readonly FakeAudioRecorder _recorder = new();
    private readonly FakeTarget _target = new();
    private readonly DictationService _service;

    public DictationServiceTests()
    {
        _service = new DictationService(_recorder, () => _asr, _messages, action => action());
    }

    [Fact]
    public async Task RecordThenStop_InsertsRecognizedText_AndPrewarmedTheModel()
    {
        List<EDictationState> states = [];
        _service.StateChanged += () => states.Add(_service.State);

        Assert.True(_service.Start(_target));
        Assert.Same(_target, _service.Target);
        await _service.StopAsync();

        Assert.Equal(["你好世界"], _target.Inserted);
        Assert.Equal([EDictationState.Recording, EDictationState.Transcribing, EDictationState.Idle], states);
        await WaitFor(() => _asr.Transcriptions.Count == 2); //一次预热静音 + 一次真识别
        Assert.Null(_service.Target);
    }

    [Fact]
    public async Task OnlineRecognizer_IsNotPrewarmed()
    {
        _asr.IsLocal = false;

        Assert.True(_service.Start(_target));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await _service.StopAsync();

        Assert.Equal(["你好世界"], _target.Inserted);
        Assert.Single(_asr.Transcriptions); //只有真识别那一次，没为预热白发请求
    }

    [Fact]
    public async Task OnlineRecognizerFails_SameRecordingGoesToLocal()
    {
        FakeAudioProvider online = new() { IsLocal = false, Error = new HttpRequestException("offline") };
        DictationService service = new(_recorder, () => online, _messages, action => action(),
            (failed, _) => failed.IsLocal ? null : _asr);

        Assert.True(service.Start(_target));
        await service.StopAsync();

        Assert.Equal(["你好世界"], _target.Inserted);
        Assert.Equal(online.Transcriptions.Single(), _asr.Transcriptions.Single()); //同一段录音从头再识别
        Assert.Single(_messages.Notifications);
    }

    [Fact]
    public async Task ShortOrSilentRecording_IsNotTranscribed()
    {
        _recorder.Recording = FakeAudioRecorder.Speech(TimeSpan.FromMilliseconds(100));
        _service.Start(_target);
        await _service.StopAsync();
        Assert.Empty(_target.Inserted);
        Assert.Empty(_messages.Notifications); //误触不打扰

        _recorder.Recording = FakeAudioRecorder.Speech(TimeSpan.FromSeconds(2), peak: 0);
        _service.Start(_target);
        await _service.StopAsync();
        Assert.Empty(_target.Inserted);
        Assert.Single(_messages.Notifications); //静音提示查麦克风权限
    }

    [Fact]
    public async Task Failures_AreExplained()
    {
        _recorder.FailToStart = true;
        Assert.False(_service.Start(_target));
        Assert.Equal(EDictationState.Idle, _service.State);

        _recorder.FailToStart = false;
        _target.IsAvailable = false; //说完时输入框已经关了：字别丢，放进提示
        _service.Start(_target);
        await _service.StopAsync();

        Assert.Equal(2, _messages.Notifications.Count);
        Assert.Contains("你好世界", _messages.Notifications[1].Message);
    }

    [Fact]
    public void ForgettingTheTarget_CancelsItsRecording()
    {
        _service.Focus(_target);
        _service.Start(_target);

        _service.Forget(_target);

        Assert.Equal(EDictationState.Idle, _service.State);
        Assert.False(_recorder.IsRecording);
        Assert.Null(_service.FocusedTarget);
    }

    [Fact]
    public async Task HoldKey_RecordsWhileHeld_AndShortTapsAreIgnored()
    {
        TimeSpan now = TimeSpan.Zero;
        SettingConfig settings = new() { DictationHoldKey = "VcRightAlt", DictationHoldEnabled = true };
        using DictationHotkey hotkey = new(_service, () => settings, action => action(), () => now);

        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Equal(EDictationState.Idle, _service.State); //没有获得焦点的输入框：不开录

        _service.Focus(_target);
        hotkey.OnKeyDown(KeyCode.VcLeftAlt); //左右分开
        Assert.Equal(EDictationState.Idle, _service.State);

        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Equal(EDictationState.Recording, _service.State);
        now += TimeSpan.FromMilliseconds(100);
        hotkey.OnKeyUp(KeyCode.VcRightAlt);
        Assert.Equal(EDictationState.Idle, _service.State); //短按当误触
        Assert.Empty(_target.Inserted);

        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        now += TimeSpan.FromSeconds(2);
        hotkey.OnKeyUp(KeyCode.VcRightAlt);
        await WaitFor(() => _target.Inserted.Count == 1);

        settings.DictationHoldEnabled = false;
        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Equal(EDictationState.Idle, _service.State);
    }

    [Theory]
    [InlineData("hello", 5, "world", true)]
    [InlineData("你好", 2, "world", false)]
    [InlineData("hello ", 6, "world", false)]
    [InlineData("", 0, "world", false)]
    public void EnglishAfterEnglish_GetsASpace(string current, int position, string text, bool expected)
    {
        Assert.Equal(expected, TextBoxDictationTarget.NeedsSpace(current, position, text));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class FakeTarget : IDictationTarget
    {
        public List<string> Inserted { get; } = [];

        public bool IsAvailable { get; set; } = true;

        public void Insert(string text) => Inserted.Add(text);
    }
}
