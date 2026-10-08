using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 用 SoundFlow 出声。整段读进内存再解码（wav / mp3 由 miniaudio 识别，裸 PCM 补文件头），解码器负责重采样到设备格式。
/// 每段现开默认输出设备、放完就关：用户中途换了耳机，下一段自然跟过去，也不常驻占着设备
/// </summary>
public sealed class SoundFlowAudioPlayer : IAudioPlayer
{
    private static readonly AudioFormat DeviceFormat = SoundFlowRuntime.FloatFormat(48000, 2);
    private static readonly TimeSpan TailDrain = TimeSpan.FromMilliseconds(120); //数据放完后设备缓冲里还有一小截，等它出声再关

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;

    public bool IsPlaying => Volatile.Read(ref _current) != null;

    public async Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken)
    {
        if (audio.Format == EAudioFormat.Aiff) throw new NotSupportedException("AIFF playback is not supported.");

        // 新的一段先把旧的（正在放的或还在排队的）打断
        CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Cancel(Interlocked.Exchange(ref _current, session));
        try
        {
            await _gate.WaitAsync(session.Token).ConfigureAwait(false);
            try
            {
                byte[] bytes = await ReadAllAsync(audio, session.Token).ConfigureAwait(false);
                return await PlayBytesAsync(bytes, session.Token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _current, null, session);
            session.Dispose();
        }
    }

    public void Stop() => Cancel(Volatile.Read(ref _current));

    private static async Task<bool> PlayBytesAsync(byte[] bytes, CancellationToken token)
    {
        AudioPlaybackDevice device;
        try
        {
            device = SoundFlowRuntime.Engine.InitializePlaybackDevice(null, DeviceFormat);
        }
        catch (Exception e) when (e is not AudioDeviceException and not OperationCanceledException)
        {
            throw new AudioDeviceException($"No playback device: {e.Message}", e);
        }

        using (device)
        {
            using MemoryStream stream = new(bytes, false);
            using StreamDataProvider provider = new(SoundFlowRuntime.Engine, DeviceFormat, stream);
            using SoundPlayer player = new(SoundFlowRuntime.Engine, DeviceFormat, provider);
            TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackEnded += (_, _) => ended.TrySetResult();
            device.MasterMixer.AddComponent(player);
            device.Start();
            player.Play();
            try
            {
                await ended.Task.WaitAsync(token).ConfigureAwait(false);
                await Task.Delay(TailDrain, CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            finally
            {
                player.Stop();
                device.MasterMixer.RemoveComponent(player);
                device.Stop();
            }
        }
    }

    private static async Task<byte[]> ReadAllAsync(AudioSynthesisResult audio, CancellationToken token)
    {
        using MemoryStream buffer = new();
        await audio.Audio.CopyToAsync(buffer, token).ConfigureAwait(false);
        if (audio.Format != EAudioFormat.Pcm16) return buffer.ToArray();
        int sampleRate = audio.SampleRateHz ?? throw new ArgumentException("Pcm16 audio needs SampleRateHz.", nameof(audio));
        return WavEncoder.WrapPcm16(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), sampleRate, 1);
    }

    // 旧会话可能刚好在收尾时被释放
    private static void Cancel(CancellationTokenSource? session)
    {
        try
        {
            session?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
