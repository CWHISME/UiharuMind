using EdgeTtsSharp;
using EdgeTtsSharp.Structures;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 免密钥的在线语音（Edge TTS，微软 Edge 朗读服务的非官方端点）。
/// 会失效是它的固有属性（ADR 0071）：调用方要有降级路径，本 provider 只保证如实失败。
/// </summary>
public sealed class EdgeTtsProvider : IAudioProvider
{
    private static readonly Dictionary<string, string> DefaultVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh"] = "zh-CN-XiaoxiaoNeural",
        ["en"] = "en-US-AriaNeural",
        ["ja"] = "ja-JP-NanamiNeural",
        ["ko"] = "ko-KR-SunHiNeural"
    };

    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(15);

    private readonly AudioServiceInfo _service;
    private readonly IEdgeTtsGateway _gateway;
    private readonly TimeSpan _idleTimeout; //多久没收到数据算卡住

    /// <param name="service">服务条目</param>
    public EdgeTtsProvider(AudioServiceInfo service) : this(service, new EdgeTtsGateway())
    {
    }

    internal EdgeTtsProvider(AudioServiceInfo service, IEdgeTtsGateway gateway, TimeSpan? idleTimeout = null)
    {
        _service = service;
        _gateway = gateway;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
    }

    public string Id => _service.Id;
    public string DisplayName => _service.Name;
    public EAudioProviderKind Kind => EAudioProviderKind.OnlineService;

    public IReadOnlySet<EAudioCapability> Capabilities { get; } =
        new HashSet<EAudioCapability> { EAudioCapability.Tts };

    public bool IsLocal => false;

    /// <summary>
    /// 没指定音色时按文字语种挑一个常用的（中文晓晓）
    /// </summary>
    /// <param name="language">语种代码；null 当中文</param>
    /// <returns>音色短名</returns>
    public static string DefaultVoiceFor(string? language) =>
        DefaultVoices.GetValueOrDefault(language ?? "zh") ?? DefaultVoices["zh"];

    public async Task<AudioSynthesisResult> SynthesizeAsync(
        AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Text is required.", nameof(request));

        string shortName = !string.IsNullOrWhiteSpace(request.Voice) ? request.Voice
            : !string.IsNullOrWhiteSpace(_service.Voice) ? _service.Voice
            : DefaultVoiceFor(request.Language);
        Voice voice = await _gateway.GetVoiceAsync(shortName, cancellationToken).ConfigureAwait(false)
                      ?? throw new NotSupportedException($"Edge TTS voice not found: {shortName}");

        int rate = request.Speed is { } speed ? (int)Math.Round((speed - 1) * 100) : 0;
        // 库的取流是同步方法，放到后台线程免得卡住调用方（常是界面线程）
        Stream audio = await Task.Run(() => _gateway.GetAudioStream(voice, request.Text, rate, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        byte[] bytes = await ReadAllAsync(audio, cancellationToken).ConfigureAwait(false);
        return new AudioSynthesisResult(new MemoryStream(bytes, false), EAudioFormat.Mp3);
    }

    // 库给的流立刻返回、数据随后才到，连接半路卡住时读它会一直等，它的读也不一定认取消：
    // 每次读都由这边限时、响应取消；放弃时在后台关流（关流也可能等），调用方立刻拿到结果
    private async Task<byte[]> ReadAllAsync(Stream audio, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[16 * 1024];
        try
        {
            while (true)
            {
                int read = await audio.ReadAsync(chunk, CancellationToken.None).AsTask()
                    .WaitAsync(_idleTimeout, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                buffer.Write(chunk, 0, read);
            }
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            _ = Task.Run(audio.Dispose);
            if (e is TimeoutException)
                throw new TimeoutException($"Edge TTS sent no audio for {_idleTimeout.TotalSeconds:0} seconds.", e);
            throw;
        }

        await audio.DisposeAsync().ConfigureAwait(false);
        return buffer.ToArray();
    }

    public Task<AudioTranscriptionResult> TranscribeAsync(
        AudioTranscriptionRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException("Edge TTS cannot transcribe.");

    public async Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken cancellationToken) =>
        await _gateway.GetVoiceNamesAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>EdgeTtsSharp 静态调用的可注入面，便于测试</summary>
internal interface IEdgeTtsGateway
{
    Task<Voice?> GetVoiceAsync(string shortName, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetVoiceNamesAsync(CancellationToken cancellationToken);

    /// <summary>同步返回：EdgeTtsSharp 的 GetAudioStream 是同步方法</summary>
    Stream GetAudioStream(Voice voice, string text, int rate, CancellationToken cancellationToken);
}

internal sealed class EdgeTtsGateway : IEdgeTtsGateway
{
    public async Task<Voice?> GetVoiceAsync(string shortName, CancellationToken cancellationToken)
    {
        return await EdgeTts.GetVoice(shortName).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetVoiceNamesAsync(CancellationToken cancellationToken)
    {
        return (await EdgeTts.GetVoices().ConfigureAwait(false)).Select(x => x.ShortName).ToList();
    }

    public Stream GetAudioStream(Voice voice, string text, int rate, CancellationToken cancellationToken)
    {
        return EdgeTts.GetAudioStream(voice, text, new PlaybackSettings { Rate = rate }, cancellationToken);
    }
}
