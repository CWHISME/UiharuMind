using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 朗读进行到哪了
/// </summary>
public enum EReadAloudState
{
    Idle,

    /// <summary>在合成第一段（首次还要拉起引擎、加载模型）</summary>
    Preparing,

    Speaking
}

/// <summary>
/// 指定这次用哪个模型、哪个音色念（如模型页的试听）；不指定就用默认的那个、本地模型再按语种挑
/// </summary>
/// <param name="Model">本地模型 id</param>
/// <param name="Voice">音色 id</param>
/// <param name="Provider">用哪个（在线服务的 id）；null 为默认</param>
public sealed record ReadAloudOptions(string? Model = null, string? Voice = null, string? Provider = null);

/// <summary>
/// 朗读：同一时刻只念一段文字，再点同一个来源就停。消息朗读与模型试听都走这里，新的一段会立刻停掉旧的
/// </summary>
public interface IReadAloudService
{
    /// <summary>
    /// 正在念的来源（如某条消息）；没在念为 null
    /// </summary>
    object? Owner { get; }

    /// <summary>
    /// 状态
    /// </summary>
    EReadAloudState State { get; }

    /// <summary>
    /// 来源或状态变了（在调用 <see cref="ToggleAsync"/> 的线程上触发）
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// 念这段；它正在念就停。念完、被停或失败才返回，失败自己弹提示
    /// </summary>
    /// <param name="owner">来源，用来认「再点一次是停」</param>
    /// <param name="text">文字（可以是 Markdown）</param>
    /// <param name="options">指定模型与音色；null 用默认</param>
    Task ToggleAsync(object owner, string text, ReadAloudOptions? options = null);

    /// <summary>
    /// 停
    /// </summary>
    void Stop();
}

/// <summary>
/// 朗读的实现：整理文字 → 切句 → 合成下一句的同时播这一句。语种交给 provider 挑会说的模型
/// </summary>
public sealed class ReadAloudService : IReadAloudService
{
    private readonly Func<string?, IAudioProvider> _tts;
    private readonly IAudioPlayer _player;
    private readonly IMessageService _messages;
    private readonly Func<IAudioProvider, string?, IAudioProvider?>? _fallback;
    private readonly Func<float>? _speed;
    private CancellationTokenSource? _session;

    /// <param name="tts">合成用的 provider：指定的 id → provider，null 为默认（每次念时现取，免得启动就碰语音引擎）</param>
    /// <param name="player">播放</param>
    /// <param name="messages">失败提示</param>
    /// <param name="fallback">默认的在线服务失败时换哪个（失败的、文字语种 → 备用，没有为 null）；null 不换</param>
    /// <param name="speed">语速倍率（每次念时现取）；null 为原速</param>
    public ReadAloudService(Func<string?, IAudioProvider> tts, IAudioPlayer player, IMessageService messages,
        Func<IAudioProvider, string?, IAudioProvider?>? fallback = null, Func<float>? speed = null)
    {
        _tts = tts;
        _player = player;
        _messages = messages;
        _fallback = fallback;
        _speed = speed;
    }

    public object? Owner { get; private set; }

    public EReadAloudState State { get; private set; }

    public event Action? StateChanged;

    public async Task ToggleAsync(object owner, string text, ReadAloudOptions? options = null)
    {
        if (ReferenceEquals(owner, Owner))
        {
            Stop();
            return;
        }

        Stop();
        string clean = SpeechText.Clean(text);
        IReadOnlyList<string> chunks = SpeechText.Split(clean);
        if (chunks.Count == 0)
        {
            _messages.ShowNotification(Loc.Text(LangKey.ReadAloudNothing));
            return;
        }

        CancellationTokenSource session = new();
        _session = session;
        SetState(owner, EReadAloudState.Preparing);
        AudioSynthesisRequest request = new("", options?.Voice, Speed(), options?.Model,
            SpeechText.DetectLanguage(clean));
        Task<AudioSynthesisResult>? next = null;
        try
        {
            IAudioProvider tts = WithFallback(_tts(options?.Provider), options);
            next = tts.SynthesizeAsync(request with { Text = chunks[0] }, session.Token);
            for (int i = 0; i < chunks.Count; i++)
            {
                AudioSynthesisResult audio = await next!; //循环里下一句总在：最后一句之前都排了
                next = i + 1 < chunks.Count ? tts.SynthesizeAsync(request with { Text = chunks[i + 1] }, session.Token) : null;
                await using (audio.Audio)
                {
                    SetState(owner, EReadAloudState.Speaking);
                    // 被别处的播放（如试听）打断也算停
                    if (!await _player.PlayAsync(audio, session.Token)) break;
                }
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            Log.Warning($"Read aloud failed: {e.Message}");
            if (_session == session)
                _messages.ShowNotification(Loc.Text(LangKey.ReadAloudFailed, AudioErrorText.Describe(e)),
                    severity: MessageSeverity.Warning);
        }
        finally
        {
            Discard(next);
            if (_session == session)
            {
                _session = null;
                SetState(null, EReadAloudState.Idle);
            }

            session.Dispose();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? session = _session;
        if (session == null) return;
        _session = null;
        session.Cancel();
        SetState(null, EReadAloudState.Idle);
    }

    private float? Speed()
    {
        float speed = Math.Clamp(_speed?.Invoke() ?? 1f, AudioSettingConfig.MinSpeed, AudioSettingConfig.MaxSpeed);
        return speed == 1f ? null : speed;
    }

    // 念的是默认的在线服务（不是试听指定的）：失败时这一段剩下的改用本地模型，提示一句
    private IAudioProvider WithFallback(IAudioProvider tts, ReadAloudOptions? options)
    {
        if (_fallback == null || tts.IsLocal || options?.Provider != null || options?.Model != null) return tts;
        return new FallbackAudioProvider(tts, language => _fallback(tts, language), (failed, e) =>
        {
            Log.Warning($"Read aloud fell back to local: {e.Message}");
            _messages.ShowNotification(Loc.Text(LangKey.ReadAloudFellBack, failed.DisplayName, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
        });
    }

    // 停在半路时下一句可能已经在合成：等它出来把流关掉，异常也在这里看掉
    private static void Discard(Task<AudioSynthesisResult>? pending)
    {
        pending?.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) t.Result.Audio.Dispose();
            else _ = t.Exception;
        }, TaskScheduler.Default);
    }

    private void SetState(object? owner, EReadAloudState state)
    {
        if (ReferenceEquals(owner, Owner) && state == State) return;
        Owner = owner;
        State = state;
        StateChanged?.Invoke();
    }
}
