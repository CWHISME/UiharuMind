using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 输入框旁的麦克风按钮：点一下开录、再点一下识别；按钮状态只跟「录给自己这个输入框」的那一路走
/// </summary>
public partial class ComposerDictationViewData : ObservableObject, IDisposable
{
    private readonly IDictationService _service;
    private readonly IDictationTarget _target;
    private readonly Func<SettingConfig> _settings;

    /// <param name="service">听写服务</param>
    /// <param name="target">本输入框</param>
    /// <param name="settings">取设置（提示里写按住说话的键）</param>
    public ComposerDictationViewData(IDictationService service, IDictationTarget target, Func<SettingConfig> settings)
    {
        _service = service;
        _target = target;
        _settings = settings;
        service.StateChanged += OnStateChanged;
        service.LevelChanged += OnLevelChanged;
    }

    public bool IsRecording => IsMine && _service.State == EDictationState.Recording;

    public bool IsTranscribing => IsMine && _service.State == EDictationState.Transcribing;

    public bool IsIdle => !IsRecording && !IsTranscribing;

    /// <summary>
    /// 录音音量（0..1），给按钮下的小音量条
    /// </summary>
    public double Level => IsRecording ? _service.Level : 0;

    /// <summary>
    /// 悬停提示：空闲时顺带告诉按住说话用哪个键
    /// </summary>
    public string Tooltip
    {
        get
        {
            if (IsRecording) return Loc.Text(LangKey.DictationRecordingTip);
            if (IsTranscribing) return Loc.Text(LangKey.DictationTranscribingTip);
            SettingConfig settings = _settings();
            return settings.DictationHoldEnabled
                ? Loc.Text(LangKey.DictationMicHoldTip,
                    DictationKeyText.Describe(DictationKeyText.Parse(settings.DictationHoldKey)))
                : Loc.Text(LangKey.DictationMicTip);
        }
    }

    private bool IsMine => ReferenceEquals(_service.Target, _target);

    public void Dispose()
    {
        _service.StateChanged -= OnStateChanged;
        _service.LevelChanged -= OnLevelChanged;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Toggle()
    {
        if (IsRecording) await _service.StopAsync();
        else if (_service.State == EDictationState.Idle)
        {
            _service.Focus(_target);
            _service.Start(_target);
        }
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsTranscribing));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(Tooltip));
    }

    private void OnLevelChanged()
    {
        if (IsRecording) OnPropertyChanged(nameof(Level));
    }
}
