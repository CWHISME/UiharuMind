using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Models.AudioModels;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 语音偏好：语速读设置、改了即存、可还原，越界的旧值收回范围内；「改用本地」默认关，勾上即存
/// </summary>
public class AudioPreferencesTests
{
    [Fact]
    public void SpeedChangeSaves_AndResetGoesBackToNormal()
    {
        AudioSettingConfig settings = new() { ReadAloudSpeed = 1.2f };
        AudioPreferencesViewData preferences = new(settings);
        Assert.Equal("1.2×", preferences.SpeedText);
        Assert.True(preferences.IsSpeedChanged);

        preferences.Speed = 0.7999999;
        Assert.Equal(0.8f, settings.ReadAloudSpeed);

        preferences.ResetSpeedCommand.Execute(null);
        Assert.Equal(1f, settings.ReadAloudSpeed);
        Assert.False(preferences.IsSpeedChanged);
        Assert.Equal("1.0×", preferences.SpeedText);

        Assert.Equal(AudioSettingConfig.MaxSpeed, new AudioPreferencesViewData(new AudioSettingConfig { ReadAloudSpeed = 9 }).Speed);
    }

    [Fact]
    public void FallbackIsOffByDefault_AndTickingSaves()
    {
        AudioSettingConfig settings = new();
        AudioPreferencesViewData preferences = new(settings);
        Assert.False(preferences.FallbackToLocal);

        preferences.FallbackToLocal = true;
        Assert.True(settings.FallbackToLocal);
    }
}
