using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 用 SoundFlow 从默认麦克风录音：设备直接开成 16 kHz 单声道（miniaudio 负责转换），采样攒在内存里，停下时编成 WAV。
/// 最长录 <see cref="MaxDuration"/>，超出的丢掉，免得忘了停把内存吃满
/// </summary>
public sealed class SoundFlowAudioRecorder : IAudioRecorder
{
    /// <summary>
    /// 采样率：语音识别模型普遍要 16 kHz
    /// </summary>
    public const int SampleRate = 16000;

    /// <summary>
    /// 最长录多久
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    private static readonly int MaxSamples = (int)(SampleRate * MaxDuration.TotalSeconds);

    private readonly Lock _gate = new();
    private AudioCaptureDevice? _device;
    private float[] _buffer = [];
    private int _length;
    private float _peak;

    public event Action<float>? LevelChanged;

    public bool IsRecording
    {
        get
        {
            lock (_gate) return _device != null;
        }
    }

    public void Start()
    {
        AudioCaptureDevice device;
        try
        {
            device = SoundFlowRuntime.Engine.InitializeCaptureDevice(null, SoundFlowRuntime.FloatFormat(SampleRate, 1));
        }
        catch (Exception e) when (e is not AudioDeviceException)
        {
            throw new AudioDeviceException($"No capture device: {e.Message}", e);
        }

        lock (_gate)
        {
            if (_device != null)
            {
                device.Dispose();
                throw new InvalidOperationException("Already recording.");
            }

            _device = device;
            _buffer = new float[SampleRate * 10];
            _length = 0;
            _peak = 0;
        }

        device.OnAudioProcessed += OnSamples;
        device.Start();
    }

    public AudioRecording? Stop()
    {
        if (!CloseDevice()) return null;
        lock (_gate)
        {
            return new AudioRecording(WavEncoder.Encode(_buffer.AsSpan(0, _length), SampleRate, 1),
                TimeSpan.FromSeconds(_length / (double)SampleRate), _peak);
        }
    }

    public void Cancel() => CloseDevice();

    // 音频线程上：攒进缓冲（不够就翻倍，到上限为止），顺带报一个峰值给界面画音量
    private void OnSamples(Span<float> samples, Capability capability)
    {
        float peak = 0;
        foreach (float sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        lock (_gate)
        {
            if (_device == null) return;
            int count = Math.Min(samples.Length, MaxSamples - _length);
            if (_length + count > _buffer.Length)
                Array.Resize(ref _buffer, Math.Min(MaxSamples, Math.Max(_buffer.Length * 2, _length + count)));
            samples[..count].CopyTo(_buffer.AsSpan(_length));
            _length += count;
            _peak = Math.Max(_peak, peak);
        }

        LevelChanged?.Invoke(Math.Min(1f, peak));
    }

    // 先在锁里摘下设备，再在锁外停：停设备会等音频线程退出，而音频线程的回调要拿同一把锁
    private bool CloseDevice()
    {
        AudioCaptureDevice? device;
        lock (_gate)
        {
            device = _device;
            _device = null;
        }

        if (device == null) return false;
        device.OnAudioProcessed -= OnSamples;
        device.Stop();
        device.Dispose();
        return true;
    }
}
