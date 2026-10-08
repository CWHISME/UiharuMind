using System;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 听写进行到哪了
/// </summary>
public enum EDictationState
{
    Idle,
    Recording,

    /// <summary>录完了，在等识别结果</summary>
    Transcribing
}

/// <summary>
/// 听写：录音 → 识别 → 交给去向。同一时刻只有一路；按钮与按住说话共用
/// </summary>
public interface IDictationService
{
    /// <summary>
    /// 状态
    /// </summary>
    EDictationState State { get; }

    /// <summary>
    /// 这一路的去向；空闲为 null
    /// </summary>
    IDictationTarget? Target { get; }

    /// <summary>
    /// 当前音量（0..1），只在录音时有意义
    /// </summary>
    float Level { get; }

    /// <summary>
    /// 最近获得焦点的去向（按住说话往这里送）；没有为 null
    /// </summary>
    IDictationTarget? FocusedTarget { get; }

    /// <summary>
    /// 状态或去向变了（界面线程上触发）
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// 音量变了（界面线程上触发，已节流）
    /// </summary>
    event Action? LevelChanged;

    /// <summary>
    /// 记下获得焦点的去向
    /// </summary>
    /// <param name="target">去向</param>
    void Focus(IDictationTarget target);

    /// <summary>
    /// 去向没了（控件卸载）：是最近焦点就清掉，正在录给它就取消
    /// </summary>
    /// <param name="target">去向</param>
    void Forget(IDictationTarget target);

    /// <summary>
    /// 开始录给这个去向
    /// </summary>
    /// <param name="target">去向</param>
    /// <returns>开始了为 true；正忙或麦克风打不开（已提示）为 false</returns>
    bool Start(IDictationTarget target);

    /// <summary>
    /// 停止录音并识别，结果送进去向；失败自己提示
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 停止并丢掉录音
    /// </summary>
    void Cancel();
}

/// <summary>
/// 听写的实现。开录就拿一小段静音去识别一次，让引擎和识别模型在用户说话时加载好（冷启动十几秒）
/// </summary>
public sealed class DictationService : IDictationService
{
    /// <summary>
    /// 短于此的录音当误触丢掉
    /// </summary>
    public static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(300);

    private readonly IAudioRecorder _recorder;
    private readonly Func<IAudioProvider> _asr;
    private readonly IMessageService _messages;
    private readonly Action<Action> _post;
    private int _isLevelPending; //音量回调在音频线程上很密，合并成一次界面刷新

    /// <param name="recorder">录音</param>
    /// <param name="asr">识别用的 provider（第一次用才取）</param>
    /// <param name="messages">失败提示</param>
    /// <param name="post">切回界面线程</param>
    public DictationService(IAudioRecorder recorder, Func<IAudioProvider> asr, IMessageService messages,
        Action<Action> post)
    {
        _recorder = recorder;
        _asr = asr;
        _messages = messages;
        _post = post;
        _recorder.LevelChanged += OnLevel;
    }

    public EDictationState State { get; private set; }

    public IDictationTarget? Target { get; private set; }

    public float Level { get; private set; }

    public IDictationTarget? FocusedTarget { get; private set; }

    public event Action? StateChanged;

    public event Action? LevelChanged;

    public void Focus(IDictationTarget target) => FocusedTarget = target;

    public void Forget(IDictationTarget target)
    {
        if (ReferenceEquals(FocusedTarget, target)) FocusedTarget = null;
        if (ReferenceEquals(Target, target) && State == EDictationState.Recording) Cancel();
    }

    public bool Start(IDictationTarget target)
    {
        if (State != EDictationState.Idle) return false;
        try
        {
            _recorder.Start();
        }
        catch (Exception e) when (e is AudioDeviceException or InvalidOperationException)
        {
            Log.Warning($"Start dictation failed: {e.Message}");
            _messages.ShowNotification(Loc.Text(LangKey.DictationFailed, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
            return false;
        }

        Level = 0;
        SetState(EDictationState.Recording, target);
        Prewarm();
        return true;
    }

    public async Task StopAsync()
    {
        if (State != EDictationState.Recording) return;
        IDictationTarget target = Target!;
        AudioRecording? recording = _recorder.Stop();
        SetState(EDictationState.Transcribing, target);
        try
        {
            if (recording == null || recording.Duration < MinDuration) return;
            if (recording.IsSilent)
            {
                _messages.ShowNotification(Loc.Text(LangKey.DictationSilent), severity: MessageSeverity.Warning);
                return;
            }

            AudioTranscriptionResult result = await _asr().TranscribeAsync(recording.ToRequest(), CancellationToken.None);
            string text = result.Text.Trim();
            if (text.Length == 0) _messages.ShowNotification(Loc.Text(LangKey.DictationNothingHeard));
            else if (target.IsAvailable) target.Insert(text);
            else _messages.ShowNotification(Loc.Text(LangKey.DictationTargetGone, text));
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            Log.Warning($"Dictation failed: {e.Message}");
            _messages.ShowNotification(Loc.Text(LangKey.DictationFailed, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
        }
        finally
        {
            SetState(EDictationState.Idle, null);
        }
    }

    public void Cancel()
    {
        if (State != EDictationState.Recording) return;
        _recorder.Cancel();
        SetState(EDictationState.Idle, null);
    }

    // 拿 0.2 秒静音识别一次：引擎没起就起、模型没载就载，等用户说完时多半已经就绪。失败不提示，真识别时再说
    private void Prewarm()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                byte[] silence = WavEncoder.Encode(new float[IAudioRecorder.SpeechSampleRate / 5],
                    IAudioRecorder.SpeechSampleRate, 1);
                using System.IO.MemoryStream audio = new(silence, false);
                await _asr().TranscribeAsync(new AudioTranscriptionRequest(audio, EAudioFormat.Wav), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Debug($"Dictation prewarm failed: {e.Message}");
            }
        });
    }

    private void OnLevel(float level)
    {
        Level = level;
        if (Interlocked.Exchange(ref _isLevelPending, 1) == 1) return;
        _post(() =>
        {
            Volatile.Write(ref _isLevelPending, 0);
            LevelChanged?.Invoke();
        });
    }

    private void SetState(EDictationState state, IDictationTarget? target)
    {
        State = state;
        Target = target;
        StateChanged?.Invoke();
    }
}
