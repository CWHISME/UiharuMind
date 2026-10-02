using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Configs;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 群聊离席的设置（ADR 0055）：三道只防失控的保险丝与没进展时的唤醒节奏，变更即存，下一次离席起生效。
/// 数值框绑的是 decimal，存回去取整
/// </summary>
public partial class GroupAwaySettingsViewData : ObservableObject
{
    private readonly SettingsWriteBack _writeBack = new(() => AgentSettingConfig.Current.Save()); //写回闸门

    [ObservableProperty] private decimal _maxHours;
    [ObservableProperty] private decimal _maxAvatarTurns;
    [ObservableProperty] private decimal _maxIdleWaves;
    [ObservableProperty] private decimal _backoffStartSeconds;
    [ObservableProperty] private decimal _backoffMaxMinutes;
    [ObservableProperty] private decimal _stopDelayMinutes;

    /// <summary>从全局设置回填（回填期间不落盘）</summary>
    public GroupAwaySettingsViewData()
    {
        using (_writeBack.BeginLoad())
        {
            AgentSettingConfig config = AgentSettingConfig.Current;
            MaxHours = config.AwayMaxHours;
            MaxAvatarTurns = config.AwayMaxAvatarTurns;
            MaxIdleWaves = config.AwayMaxIdleWaves;
            BackoffStartSeconds = config.AwayBackoffStartSeconds;
            BackoffMaxMinutes = config.AwayBackoffMaxMinutes;
            StopDelayMinutes = config.AwayStopDelayMinutes;
        }
    }

    partial void OnMaxHoursChanged(decimal value) => Save(c => c.AwayMaxHours = (int)value);

    partial void OnMaxAvatarTurnsChanged(decimal value) => Save(c => c.AwayMaxAvatarTurns = (int)value);

    partial void OnMaxIdleWavesChanged(decimal value) => Save(c => c.AwayMaxIdleWaves = (int)value);

    partial void OnBackoffStartSecondsChanged(decimal value) => Save(c => c.AwayBackoffStartSeconds = (int)value);

    partial void OnBackoffMaxMinutesChanged(decimal value) => Save(c => c.AwayBackoffMaxMinutes = (int)value);

    partial void OnStopDelayMinutesChanged(decimal value) => Save(c => c.AwayStopDelayMinutes = (int)value);

    private void Save(Action<AgentSettingConfig> apply)
    {
        apply(AgentSettingConfig.Current);
        _writeBack.Save();
    }
}
