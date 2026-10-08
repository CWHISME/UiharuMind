using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Features.Models.AudioModels;

/// <summary>
/// 「语音模型」页签上的两项偏好：朗读语速、在线服务连不上时改用本地模型。改了即存，下次朗读、听写起生效
/// </summary>
public partial class AudioPreferencesViewData : ObservableObject
{
    private readonly AudioSettingConfig _settings;

    [ObservableProperty] private double _speed;
    [ObservableProperty] private bool _fallbackToLocal;

    /// <param name="settings">存在这里</param>
    public AudioPreferencesViewData(AudioSettingConfig settings)
    {
        _settings = settings;
        _speed = Math.Clamp(settings.ReadAloudSpeed, AudioSettingConfig.MinSpeed, AudioSettingConfig.MaxSpeed);
        _fallbackToLocal = settings.FallbackToLocal;
    }

    public double Minimum => AudioSettingConfig.MinSpeed;

    public double Maximum => AudioSettingConfig.MaxSpeed;

    /// <summary>
    /// 形如「1.2×」
    /// </summary>
    public string SpeedText => $"{Speed:0.0#}×";

    /// <summary>
    /// 语速不是原速
    /// </summary>
    public bool IsSpeedChanged => Math.Abs(Speed - 1) > 0.001;

    partial void OnSpeedChanged(double value)
    {
        float speed = (float)Math.Round(value, 2);
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(IsSpeedChanged));
        if (_settings.ReadAloudSpeed == speed) return;
        _settings.ReadAloudSpeed = speed;
        _settings.Save();
    }

    partial void OnFallbackToLocalChanged(bool value)
    {
        _settings.FallbackToLocal = value;
        _settings.Save();
    }

    [RelayCommand]
    private void ResetSpeed() => Speed = 1;
}
