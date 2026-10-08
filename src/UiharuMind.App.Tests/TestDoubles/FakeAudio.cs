using UiharuMind.Core.AI.Audio;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 不出声的合成：记下请求，回一小段静音 WAV；设了 <see cref="Error"/> 就抛它
/// </summary>
internal sealed class FakeAudioProvider : IAudioProvider
{
    public List<AudioSynthesisRequest> Requests { get; } = [];

    /// <summary>听写过的音频（字节）</summary>
    public List<byte[]> Transcriptions { get; } = [];

    /// <summary>听写回的文字</summary>
    public string TranscribedText { get; set; } = "";

    public Exception? Error { get; set; }

    public string Id => "fake";

    public string DisplayName => "fake";

    public EAudioProviderKind Kind => EAudioProviderKind.LocalEngine;

    public IReadOnlySet<EAudioCapability> Capabilities { get; } = new HashSet<EAudioCapability> { EAudioCapability.Tts };

    public bool IsLocal => true;

    public Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Error != null) return Task.FromException<AudioSynthesisResult>(Error);
        return Task.FromResult(new AudioSynthesisResult(new MemoryStream(WavEncoder.Encode(new float[160], 16000, 1)),
            EAudioFormat.Wav));
    }

    public async Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await request.Audio.CopyToAsync(buffer, cancellationToken);
        lock (Transcriptions) Transcriptions.Add(buffer.ToArray());
        if (Error != null) throw Error;
        return new AudioTranscriptionResult(TranscribedText);
    }
}

/// <summary>
/// 不碰麦克风的录音：停下时交出给定的那段；可设成打不开麦克风
/// </summary>
internal sealed class FakeAudioRecorder : IAudioRecorder
{
    public AudioRecording Recording { get; set; } = Speech(TimeSpan.FromSeconds(1));

    public bool FailToStart { get; set; }

    public bool IsRecording { get; private set; }

    public event Action<float>? LevelChanged;

    public int SampleRate { get; private set; }

    public void Start(int sampleRate = IAudioRecorder.SpeechSampleRate)
    {
        if (FailToStart) throw new AudioDeviceException("no microphone");
        SampleRate = sampleRate;
        IsRecording = true;
        LevelChanged?.Invoke(0.5f);
    }

    public AudioRecording? Stop()
    {
        if (!IsRecording) return null;
        IsRecording = false;
        return Recording;
    }

    public void Cancel() => IsRecording = false;

    /// <summary>一段有声音的录音</summary>
    public static AudioRecording Speech(TimeSpan duration, float peak = 0.4f) =>
        new(WavEncoder.Encode(new float[(int)(16000 * duration.TotalSeconds)], 16000, 1), duration, peak);
}

/// <summary>
/// 不出声的播放：记下放过的格式与字节数，立刻放完
/// </summary>
internal sealed class RecordingAudioPlayer : IAudioPlayer
{
    public List<(EAudioFormat Format, long Bytes)> Played { get; } = [];

    public bool IsPlaying => false;

    public async Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await audio.Audio.CopyToAsync(buffer, cancellationToken);
        Played.Add((audio.Format, buffer.Length));
        return true;
    }

    public void Stop()
    {
    }
}
