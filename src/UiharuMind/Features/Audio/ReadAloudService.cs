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
/// 朗读：同一时刻只念一段文字，再点同一个来源就停
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
    Task ToggleAsync(object owner, string text);

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
    private readonly Func<IAudioProvider> _tts;
    private readonly IAudioPlayer _player;
    private readonly IMessageService _messages;
    private CancellationTokenSource? _session;

    /// <param name="tts">合成用的 provider（第一次念时才取，免得启动就碰语音引擎）</param>
    /// <param name="player">播放</param>
    /// <param name="messages">失败提示</param>
    public ReadAloudService(Func<IAudioProvider> tts, IAudioPlayer player, IMessageService messages)
    {
        _tts = tts;
        _player = player;
        _messages = messages;
    }

    public object? Owner { get; private set; }

    public EReadAloudState State { get; private set; }

    public event Action? StateChanged;

    public async Task ToggleAsync(object owner, string text)
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
        string? language = SpeechText.DetectLanguage(clean);
        Task<AudioSynthesisResult>? next = null;
        try
        {
            IAudioProvider tts = _tts();
            next = Synthesize(tts, chunks[0], language, session.Token);
            for (int i = 0; i < chunks.Count; i++)
            {
                AudioSynthesisResult audio = await next!; //循环里下一句总在：最后一句之前都排了
                next = i + 1 < chunks.Count ? Synthesize(tts, chunks[i + 1], language, session.Token) : null;
                await using (audio.Audio)
                {
                    SetState(owner, EReadAloudState.Speaking);
                    // 被别处的播放（如试听）打断也算停
                    if (!await _player.PlayAsync(audio, session.Token)) break;
                }
            }
        }
        catch (OperationCanceledException)
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

    private static Task<AudioSynthesisResult> Synthesize(IAudioProvider tts, string text, string? language,
        CancellationToken token) =>
        tts.SynthesizeAsync(new AudioSynthesisRequest(text, Language: language), token);

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
