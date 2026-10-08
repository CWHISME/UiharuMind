using System;
using SharpHook.Data;
using UiharuMind.Core.Core;
using UiharuMind.Core.Input;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 按住说话：按下设置里那个键开录，松开就识别；不到 <see cref="DictationService.MinDuration"/> 的短按当误触取消。
/// 先送本应用最近获得焦点、且窗口在前台的输入框；没有、且设置里开了「别的应用也能用」，就送当时在前台的别的应用。
/// 键与开关每次都从设置读，改了不必重启
/// </summary>
public sealed class DictationHotkey : IDisposable
{
    private readonly InputManager? _input;
    private readonly IDictationService _service;
    private readonly Func<SettingConfig> _settings;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan> _clock;
    private readonly IDictationTarget? _otherApps;
    private TimeSpan _pressedAt;
    private bool _isHolding; //这一路是按住说话开的（不是按钮开的）

    /// <param name="input">全局键盘钩子</param>
    /// <param name="service">听写服务</param>
    /// <param name="settings">取设置</param>
    /// <param name="post">切回界面线程（钩子在后台线程上回调）</param>
    /// <param name="otherApps">别的应用在前台时的去向；null 不支持</param>
    public DictationHotkey(InputManager input, IDictationService service, Func<SettingConfig> settings,
        Action<Action> post, IDictationTarget? otherApps = null)
        : this(service, settings, post, () => TimeSpan.FromMilliseconds(Environment.TickCount64), otherApps)
    {
        _input = input;
        input.EventOnKeyDown += OnKeyDown;
        input.EventOnKeyUp += OnKeyUp;
    }

    /// <summary>
    /// 不挂钩子（测试直接喂按键）
    /// </summary>
    internal DictationHotkey(IDictationService service, Func<SettingConfig> settings, Action<Action> post,
        Func<TimeSpan> clock, IDictationTarget? otherApps = null)
    {
        _otherApps = otherApps;
        _service = service;
        _settings = settings;
        _post = post;
        _clock = clock;
    }

    public void Dispose()
    {
        if (_input == null) return;
        _input.EventOnKeyDown -= OnKeyDown;
        _input.EventOnKeyUp -= OnKeyUp;
    }

    internal void OnKeyDown(KeyCode key)
    {
        if (!IsHoldKey(key)) return;
        _post(() =>
        {
            if (PickTarget() is not { } target || !_service.Start(target)) return;
            _isHolding = true;
            _pressedAt = _clock();
        });
    }

    internal void OnKeyUp(KeyCode key)
    {
        if (!IsHoldKey(key)) return;
        _post(() =>
        {
            if (!_isHolding) return;
            _isHolding = false;
            if (_clock() - _pressedAt < DictationService.MinDuration) _service.Cancel();
            else _ = _service.StopAsync();
        });
    }

    private IDictationTarget? PickTarget()
    {
        if (_service.FocusedTarget is { IsAvailable: true } focused) return focused;
        return _settings().DictationInOtherApps && _otherApps is { IsAvailable: true } other ? other : null;
    }

    private bool IsHoldKey(KeyCode key)
    {
        SettingConfig settings = _settings();
        return settings.DictationHoldEnabled && key != KeyCode.VcUndefined &&
               key == DictationKeyText.Parse(settings.DictationHoldKey);
    }
}
