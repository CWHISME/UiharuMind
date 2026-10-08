using System;
using SharpHook.Data;
using UiharuMind.Core.Core;
using UiharuMind.Core.Input;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 按住说话：按下设置里那个键开录，松开就识别；不到 <see cref="DictationService.MinDuration"/> 的短按当误触取消。
/// 只往最近获得焦点、且窗口在前台的去向送（目前即本应用的输入框）；键每次都从设置读，改了不必重启
/// </summary>
public sealed class DictationHotkey : IDisposable
{
    private readonly InputManager? _input;
    private readonly IDictationService _service;
    private readonly Func<SettingConfig> _settings;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan> _clock;
    private TimeSpan _pressedAt;
    private bool _isHolding; //这一路是按住说话开的（不是按钮开的）

    /// <param name="input">全局键盘钩子</param>
    /// <param name="service">听写服务</param>
    /// <param name="settings">取设置</param>
    /// <param name="post">切回界面线程（钩子在后台线程上回调）</param>
    public DictationHotkey(InputManager input, IDictationService service, Func<SettingConfig> settings,
        Action<Action> post) : this(service, settings, post, () => TimeSpan.FromMilliseconds(Environment.TickCount64))
    {
        _input = input;
        input.EventOnKeyDown += OnKeyDown;
        input.EventOnKeyUp += OnKeyUp;
    }

    /// <summary>
    /// 不挂钩子（测试直接喂按键）
    /// </summary>
    internal DictationHotkey(IDictationService service, Func<SettingConfig> settings, Action<Action> post,
        Func<TimeSpan> clock)
    {
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
            if (_service.FocusedTarget is not { IsAvailable: true } target) return;
            if (!_service.Start(target)) return;
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

    private bool IsHoldKey(KeyCode key)
    {
        SettingConfig settings = _settings();
        return settings.DictationHoldEnabled && key != KeyCode.VcUndefined &&
               key == DictationKeyText.Parse(settings.DictationHoldKey);
    }
}
