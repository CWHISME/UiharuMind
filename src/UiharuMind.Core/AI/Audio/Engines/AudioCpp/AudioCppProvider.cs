namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 本地引擎 provider（ADR 0071 的 LocalEngine 类）：用哪个模型、哪个音色每次请求现取设置，
/// 进程归 <see cref="AudioCppEngine"/>，本类只管把请求打到服务上
/// </summary>
public sealed class AudioCppProvider : IAudioProvider, IDisposable
{
    private readonly AudioCppEngine _engine;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private OpenAiCompatibleAudioClient? _client; //随 server 地址换，共用 _http，换掉不影响在途请求

    public AudioCppProvider(AudioCppEngine engine)
    {
        _engine = engine;
    }

    public string Id => "AudioCpp";
    public string DisplayName => "audio.cpp";
    public EAudioProviderKind Kind => EAudioProviderKind.LocalEngine;

    public IReadOnlySet<EAudioCapability> Capabilities { get; } =
        new HashSet<EAudioCapability> { EAudioCapability.Tts, EAudioCapability.Asr };

    public bool IsLocal => true;

    /// <summary>
    /// 合成。没指定音色时用设置里给这个模型选的；也没选就取服务列出的第一个（有的模型不给音色直接报错）
    /// </summary>
    public async Task<AudioSynthesisResult> SynthesizeAsync(
        AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Text is required.", nameof(request));

        AudioCppModelEntry model = _engine.ResolveModel(EAudioCppTask.Tts);
        OpenAiCompatibleAudioClient client = await GetClientAsync(model, cancellationToken).ConfigureAwait(false);
        string? voice = request.Voice;
        if (string.IsNullOrWhiteSpace(voice) && !_engine.Settings.TtsVoices.TryGetValue(model.Id, out voice))
            voice = (await client.GetVoicesAsync(model.Id, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return await client.SynthesizeAsync(model.Id, request.Text, voice, EAudioFormat.Wav, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AudioTranscriptionResult> TranscribeAsync(
        AudioTranscriptionRequest request, CancellationToken cancellationToken)
    {
        AudioCppModelEntry model = _engine.ResolveModel(EAudioCppTask.Asr);
        OpenAiCompatibleAudioClient client = await GetClientAsync(model, cancellationToken).ConfigureAwait(false);
        return await client.TranscribeAsync(model.Id, request.Audio, request.Format, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 当前合成模型的音色
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        AudioCppModelEntry model = _engine.ResolveModel(EAudioCppTask.Tts);
        OpenAiCompatibleAudioClient client = await GetClientAsync(model, cancellationToken).ConfigureAwait(false);
        return await client.GetVoicesAsync(model.Id, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private async Task<OpenAiCompatibleAudioClient> GetClientAsync(
        AudioCppModelEntry model, CancellationToken cancellationToken)
    {
        Uri baseUri = await _engine.GetServerAsync(model, cancellationToken).ConfigureAwait(false);
        OpenAiCompatibleAudioClient? client = _client;
        if (client?.BaseUrl == baseUri.AbsoluteUri) return client;
        client = new OpenAiCompatibleAudioClient(new OpenAiAudioEndpoint(baseUri.AbsoluteUri), _http);
        _client = client;
        return client;
    }
}
