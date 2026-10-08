namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 本地引擎 provider（ADR 0071 的 LocalEngine 类）：用哪个模型、哪个音色每次请求现取设置，
/// 进程归 <see cref="AudioCppEngine"/>，本类只管把请求打到服务上
/// </summary>
public sealed class AudioCppProvider : IAudioProvider, IDisposable
{
    private static readonly Lazy<AudioCppProvider> SharedInstance = new(() => new AudioCppProvider(AudioCppEngine.Shared));

    private readonly AudioCppEngine _engine;
    private readonly AudioCppVoiceResolver _voices;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private OpenAiCompatibleAudioClient? _client; //随 server 地址换，共用 _http，换掉不影响在途请求

    /// <summary>
    /// 走应用档案的那一个：朗读、听写、语音模型页签与智能体合成共用（第一次用才建）
    /// </summary>
    public static AudioCppProvider Shared => SharedInstance.Value;

    public AudioCppProvider(AudioCppEngine engine)
    {
        _engine = engine;
        _voices = new AudioCppVoiceResolver(engine);
    }

    /// <summary>
    /// 本地引擎的 provider id（<see cref="SpeechStyle.Provider"/> 指定用本地时填它）
    /// </summary>
    public const string LocalId = "AudioCpp";

    public string Id => LocalId;
    public string DisplayName => "audio.cpp";
    public EAudioProviderKind Kind => EAudioProviderKind.LocalEngine;

    public IReadOnlySet<EAudioCapability> Capabilities { get; } =
        new HashSet<EAudioCapability> { EAudioCapability.Tts, EAudioCapability.Asr };

    public bool IsLocal => true;

    /// <summary>
    /// 合成。没指定音色时用设置里给这个模型选的；也没选就取模型自带的第一个（有的模型不给音色直接报错）；
    /// 会克隆却没自带音色的，第一次用时给它生成一个固定音色存进音色库
    /// </summary>
    /// <exception cref="AudioVoiceRequiredException">模型只会克隆，音色库却是空的</exception>
    public async Task<AudioSynthesisResult> SynthesizeAsync(
        AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Text is required.", nameof(request));

        AudioCppModelEntry model = string.IsNullOrEmpty(request.Model)
            ? _engine.ResolveModel(EAudioCppTask.Tts, request.Language)
            : FindModel(request.Model);
        OpenAiCompatibleAudioClient client = await GetClientAsync(model, cancellationToken).ConfigureAwait(false);
        string? voice = await _voices.ResolveAsync(model, request.Voice, client, cancellationToken).ConfigureAwait(false);
        // 多数模型不会变速（server 对非 1 的 speed 直接报错），交给播放时变速；语气描述只给听得懂的
        AudioCppFamily? family = _engine.GetFamily(model);
        float speed = request.Speed ?? 1f;
        bool native = speed != 1f && family?.AcceptsSpeed == true;
        Dictionary<string, string>? options = !string.IsNullOrWhiteSpace(request.Instruction) &&
                                              family?.InstructionOption is { } option
            ? new Dictionary<string, string> { [option] = request.Instruction }
            : null;
        AudioSynthesisResult result = await client.SynthesizeAsync(model.Id, request.Text, voice, EAudioFormat.Wav,
            cancellationToken, speed: native ? speed : null, options: options).ConfigureAwait(false);
        return native || speed == 1f ? result : result with { PlaybackSpeed = speed };
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
    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken cancellationToken) =>
        GetVoicesAsync(_engine.ResolveModel(EAudioCppTask.Tts), cancellationToken);

    /// <summary>
    /// 某个本地合成模型自带的音色，不含音色库（会按需拉起引擎）
    /// </summary>
    /// <param name="modelId">模型 id</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>音色 id</returns>
    /// <exception cref="AudioModelMissingException">本机没有这个模型</exception>
    public Task<IReadOnlyList<string>> GetVoicesAsync(string modelId, CancellationToken cancellationToken) =>
        GetVoicesAsync(FindModel(modelId), cancellationToken);

    // 缓存里没有就重扫一次（可能刚下完）
    private AudioCppModelEntry FindModel(string modelId) =>
        _engine.GetModels().FirstOrDefault(x => x.Id == modelId) ??
        _engine.GetModels(true).FirstOrDefault(x => x.Id == modelId) ??
        throw new AudioModelMissingException(EAudioCppTask.Tts);

    /// <summary>
    /// 抽一个音色：让模型不带参考音频、用随机种子念一句参考句。满意就存进音色库，之后照它克隆，声音就固定了
    /// </summary>
    /// <param name="modelId">会克隆的合成模型</param>
    /// <param name="seed">随机种子：换一个就是另一个声音，同一个种子抽出来的一样</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>抽到的音色</returns>
    public async Task<VoiceDraft> DrawVoiceAsync(string modelId, int seed, CancellationToken cancellationToken)
    {
        AudioCppModelEntry model = FindModel(modelId);
        OpenAiCompatibleAudioClient client = await GetClientAsync(model, cancellationToken).ConfigureAwait(false);
        return await _voices.DrawAsync(model, seed, client, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetVoicesAsync(AudioCppModelEntry model, CancellationToken cancellationToken)
    {
        OpenAiCompatibleAudioClient client = await GetClientAsync(model, cancellationToken).ConfigureAwait(false);
        return await _voices.ListNativeAsync(model, client, cancellationToken).ConfigureAwait(false);
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
