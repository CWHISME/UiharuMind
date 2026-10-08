using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Features.Models.AudioModels.Voices;

/// <summary>
/// 试听一段参考音频：同一时刻只放一段，再点同一段即停，点别的就换过去
/// </summary>
public sealed class VoiceClipPlayer
{
    private readonly IAudioPlayer _player;
    private CancellationTokenSource? _current;

    /// <param name="player">出声</param>
    public VoiceClipPlayer(IAudioPlayer player)
    {
        _player = player;
    }

    /// <summary>
    /// 正在放的那段属于谁；没在放为 null
    /// </summary>
    public object? Owner { get; private set; }

    /// <summary>
    /// 开始或停止播放
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 放这段；它正在放就停
    /// </summary>
    /// <param name="owner">这段属于谁（界面据此亮停止键）</param>
    /// <param name="wav">WAV 字节</param>
    /// <exception cref="AudioDeviceException">没有输出设备</exception>
    public async Task ToggleAsync(object owner, byte[] wav)
    {
        bool same = ReferenceEquals(Owner, owner);
        Stop();
        if (same) return;

        CancellationTokenSource session = new();
        _current = session;
        Owner = owner;
        Changed?.Invoke();
        try
        {
            await _player.PlayAsync(new AudioSynthesisResult(new MemoryStream(wav, false), EAudioFormat.Wav), session.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_current, session))
            {
                _current = null;
                Owner = null;
                Changed?.Invoke();
            }

            session.Dispose();
        }
    }

    /// <summary>
    /// 停下正在放的
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? current = _current;
        if (current == null) return;
        _current = null;
        Owner = null;
        current.Cancel();
        Changed?.Invoke();
    }
}
