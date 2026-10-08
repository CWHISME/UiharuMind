using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 在别的应用里听写时的悬浮提示：本应用不在前台，没有输入框上的麦克风可看，得另给一个「在听 / 在识别」
/// </summary>
public sealed class DictationIndicatorViewData : ObservableObject, IDisposable
{
    private readonly IDictationService _service;
    private readonly IDictationTarget _watched;
    private bool _wasActive;

    /// <param name="service">听写服务</param>
    /// <param name="watched">只在录给这个去向时显示（本应用的输入框自己有状态）</param>
    public DictationIndicatorViewData(IDictationService service, IDictationTarget watched)
    {
        _service = service;
        _watched = watched;
        service.StateChanged += OnStateChanged;
        service.LevelChanged += OnLevelChanged;
    }

    /// <summary>
    /// 该显示（正在录给或识别给这个去向）
    /// </summary>
    public bool IsActive => _service.State != EDictationState.Idle && ReferenceEquals(_service.Target, _watched);

    public bool IsRecording => IsActive && _service.State == EDictationState.Recording;

    public bool IsTranscribing => IsActive && _service.State == EDictationState.Transcribing;

    /// <summary>
    /// 音量（0..100）
    /// </summary>
    public double Level => Math.Clamp(_service.Level * 100, 0, 100);

    /// <summary>
    /// 该显示 / 该收起变了（界面线程上触发）
    /// </summary>
    public event Action? ActiveChanged;

    public void Dispose()
    {
        _service.StateChanged -= OnStateChanged;
        _service.LevelChanged -= OnLevelChanged;
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsTranscribing));
        if (IsActive == _wasActive) return;
        _wasActive = IsActive;
        ActiveChanged?.Invoke();
    }

    private void OnLevelChanged() => OnPropertyChanged(nameof(Level));
}
