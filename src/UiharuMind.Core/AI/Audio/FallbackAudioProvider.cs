using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 在线服务连不上、超时时退到备用的（通常是本地引擎）。管一路（一次朗读、一次听写）：失败一次就换过去，这一路之后都用备用的，
/// 不再来回试。只认网络类失败：密钥错、余额不足、限流是要用户去处理的，照原样失败
/// </summary>
public sealed class FallbackAudioProvider : IAudioProvider
{
    private readonly IAudioProvider _primary;
    private readonly Func<string?, IAudioProvider?> _fallback;
    private readonly Action<IAudioProvider, Exception> _onFallback;
    private IAudioProvider _current;

    /// <param name="primary">先用的</param>
    /// <param name="fallback">失败时换哪个：文字语种（听写为 null）→ 备用的；null 为没得换（照原样失败）</param>
    /// <param name="onFallback">换过去时通知一次：失败的那个、失败原因</param>
    public FallbackAudioProvider(IAudioProvider primary, Func<string?, IAudioProvider?> fallback,
        Action<IAudioProvider, Exception> onFallback)
    {
        _primary = primary;
        _fallback = fallback;
        _onFallback = onFallback;
        _current = primary;
    }

    /// <summary>
    /// 眼下在用的
    /// </summary>
    public IAudioProvider Current => Volatile.Read(ref _current);

    public string Id => Current.Id;
    public string DisplayName => Current.DisplayName;
    public EAudioProviderKind Kind => Current.Kind;
    public IReadOnlySet<EAudioCapability> Capabilities => Current.Capabilities;
    public bool IsLocal => Current.IsLocal;

    public async Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        IAudioProvider current = Current;
        try
        {
            return await current.SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (CanFallBack(current, e, cancellationToken))
        {
            IAudioProvider backup = SwitchOrRethrow(e, request.Language);
            // 音色、模型 id 是在线服务那边的，备用的不认
            return await backup.SynthesizeAsync(request with { Voice = null, Model = null }, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        IAudioProvider current = Current;
        try
        {
            return await current.TranscribeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (CanFallBack(current, e, cancellationToken) && request.Audio.CanSeek)
        {
            IAudioProvider backup = SwitchOrRethrow(e, null);
            request.Audio.Position = 0; //同一段录音再识别一次
            return await backup.TranscribeAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken cancellationToken) =>
        Current.GetVoicesAsync(cancellationToken);

    /// <summary>
    /// 是不是网络类失败（连不上、超时、断线、服务端 5xx）
    /// </summary>
    /// <param name="e">异常</param>
    /// <param name="cancellationToken">调用方的取消：它触发的是用户停下，不算</param>
    /// <returns>是为 true</returns>
    public static bool IsNetworkFailure(Exception e, CancellationToken cancellationToken) => e switch
    {
        OperationCanceledException => !cancellationToken.IsCancellationRequested, //HttpClient 超时
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500,
        TimeoutException or WebSocketException or IOException => true,
        _ => false
    };

    private bool CanFallBack(IAudioProvider current, Exception e, CancellationToken cancellationToken) =>
        ReferenceEquals(current, _primary) && IsNetworkFailure(e, cancellationToken);

    private IAudioProvider SwitchOrRethrow(Exception e, string? language)
    {
        IAudioProvider? backup = _fallback(language);
        if (backup == null || ReferenceEquals(backup, _primary)) ExceptionDispatchInfo.Throw(e);

        if (ReferenceEquals(Interlocked.CompareExchange(ref _current, backup, _primary), _primary))
            _onFallback(_primary, e);
        return Current;
    }
}
