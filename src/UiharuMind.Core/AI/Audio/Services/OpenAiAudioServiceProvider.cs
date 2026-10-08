namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一个 OpenAI 兼容音频服务条目对应的 provider（照 ADR 0052：服务单独登记，按条目建 provider）。
/// 条目只做一件事，能力位随它；返回的流由调用方负责释放
/// </summary>
public sealed class OpenAiAudioServiceProvider : IAudioProvider, IDisposable
{
    private readonly AudioServiceInfo _service;
    private readonly OpenAiCompatibleAudioClient _client;
    private readonly HttpClient? _ownedHttp; //没传进来就自己建、自己释放
    private readonly EAudioFormat _format;

    /// <param name="service">服务条目</param>
    /// <param name="http">发请求用的；null 按条目的超时新建一个</param>
    public OpenAiAudioServiceProvider(AudioServiceInfo service, HttpClient? http = null)
    {
        _service = service;
        if (http == null) _ownedHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, service.TimeoutSeconds)) };
        _client = new OpenAiCompatibleAudioClient(new OpenAiAudioEndpoint(service.Endpoint, service.ApiKey),
            http ?? _ownedHttp);
        _format = service.Format.Equals("wav", StringComparison.OrdinalIgnoreCase) ? EAudioFormat.Wav : EAudioFormat.Mp3;
        Capabilities = new HashSet<EAudioCapability> { service.Task };
    }

    public string Id => _service.Id;
    public string DisplayName => _service.Name;
    public EAudioProviderKind Kind => EAudioProviderKind.OnlineService;
    public IReadOnlySet<EAudioCapability> Capabilities { get; }
    public bool IsLocal => false;

    public Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        if (_service.Task != EAudioCapability.Tts) throw new NotSupportedException($"'{_service.Name}' cannot synthesize.");
        if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Text is required.", nameof(request));
        string? voice = !string.IsNullOrWhiteSpace(request.Voice) ? request.Voice
            : string.IsNullOrWhiteSpace(_service.Voice) ? null : _service.Voice;
        return _client.SynthesizeAsync(_service.ModelId, request.Text, voice, _format, cancellationToken);
    }

    public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (_service.Task != EAudioCapability.Asr) throw new NotSupportedException($"'{_service.Name}' cannot transcribe.");
        return _client.TranscribeAsync(_service.ModelId, request.Audio, request.Format, cancellationToken);
    }

    public void Dispose() => _ownedHttp?.Dispose();
}

/// <summary>按接口格式把一个远程音频服务条目变成 provider（一种格式一个适配器）</summary>
public static class AudioServiceProviderFactory
{
    /// <summary>
    /// 建 provider
    /// </summary>
    /// <param name="service">服务条目</param>
    /// <returns>provider</returns>
    public static IAudioProvider Create(AudioServiceInfo service) => service.Dialect switch
    {
        EAudioDialect.OpenAICompatible => new OpenAiAudioServiceProvider(service),
        EAudioDialect.EdgeTts => new EdgeTtsProvider(service),
        _ => throw new ArgumentOutOfRangeException(nameof(service), service.Dialect, null)
    };
}
