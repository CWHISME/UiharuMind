using UiharuMind.Core.AI.Audio;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 不出声的合成：记下请求，回一小段静音 WAV；设了 <see cref="Error"/> 就抛它
/// </summary>
internal sealed class FakeAudioProvider : IAudioProvider
{
    public List<AudioSynthesisRequest> Requests { get; } = [];

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

    public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
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
