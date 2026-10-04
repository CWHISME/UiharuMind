using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Configs;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Generated;

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

    /// <summary>最近一次改动的反馈文本（如「已保存」），空串不显示</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    // 「值≠出厂值」才显示单项恢复默认按钮（SettingsRow 可见性绑定用）；出厂值引用 AgentSettingConfig 常量
    public bool IsMaxHoursNotDefault => MaxHours != AgentSettingConfig.FactoryDefaultAwayMaxHours;
    public bool IsMaxAvatarTurnsNotDefault => MaxAvatarTurns != AgentSettingConfig.FactoryDefaultAwayMaxAvatarTurns;
    public bool IsMaxIdleWavesNotDefault => MaxIdleWaves != AgentSettingConfig.FactoryDefaultAwayMaxIdleWaves;
    public bool IsBackoffStartSecondsNotDefault => BackoffStartSeconds != AgentSettingConfig.FactoryDefaultAwayBackoffStartSeconds;
    public bool IsBackoffMaxMinutesNotDefault => BackoffMaxMinutes != AgentSettingConfig.FactoryDefaultAwayBackoffMaxMinutes;
    public bool IsStopDelayMinutesNotDefault => StopDelayMinutes != AgentSettingConfig.FactoryDefaultAwayStopDelayMinutes;

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

    partial void OnMaxHoursChanged(decimal value)
    {
        Save(c => c.AwayMaxHours = (int)value);
        OnPropertyChanged(nameof(IsMaxHoursNotDefault));
    }

    partial void OnMaxAvatarTurnsChanged(decimal value)
    {
        Save(c => c.AwayMaxAvatarTurns = (int)value);
        OnPropertyChanged(nameof(IsMaxAvatarTurnsNotDefault));
    }

    partial void OnMaxIdleWavesChanged(decimal value)
    {
        Save(c => c.AwayMaxIdleWaves = (int)value);
        OnPropertyChanged(nameof(IsMaxIdleWavesNotDefault));
    }

    partial void OnBackoffStartSecondsChanged(decimal value)
    {
        Save(c => c.AwayBackoffStartSeconds = (int)value);
        OnPropertyChanged(nameof(IsBackoffStartSecondsNotDefault));
    }

    partial void OnBackoffMaxMinutesChanged(decimal value)
    {
        Save(c => c.AwayBackoffMaxMinutes = (int)value);
        OnPropertyChanged(nameof(IsBackoffMaxMinutesNotDefault));
    }

    partial void OnStopDelayMinutesChanged(decimal value)
    {
        Save(c => c.AwayStopDelayMinutes = (int)value);
        OnPropertyChanged(nameof(IsStopDelayMinutesNotDefault));
    }

    private void Save(Action<AgentSettingConfig> apply)
    {
        apply(AgentSettingConfig.Current);
        _writeBack.Save();
        // 回填时 IsLoading 为真，不弹反馈；只有用户真的改了才提示
        if (!_writeBack.IsLoading) StatusText = Loc.Text(LangKey.ShortcutSavedTips);
    }

    // 单项恢复默认：出厂值引用 AgentSettingConfig 常量，不手抄数字；走属性赋值，handler 统一保存+反馈
    [RelayCommand] private void ResetMaxHours() => MaxHours = AgentSettingConfig.FactoryDefaultAwayMaxHours;
    [RelayCommand] private void ResetMaxAvatarTurns() => MaxAvatarTurns = AgentSettingConfig.FactoryDefaultAwayMaxAvatarTurns;
    [RelayCommand] private void ResetMaxIdleWaves() => MaxIdleWaves = AgentSettingConfig.FactoryDefaultAwayMaxIdleWaves;
    [RelayCommand] private void ResetBackoffStartSeconds() => BackoffStartSeconds = AgentSettingConfig.FactoryDefaultAwayBackoffStartSeconds;
    [RelayCommand] private void ResetBackoffMaxMinutes() => BackoffMaxMinutes = AgentSettingConfig.FactoryDefaultAwayBackoffMaxMinutes;
    [RelayCommand] private void ResetStopDelayMinutes() => StopDelayMinutes = AgentSettingConfig.FactoryDefaultAwayStopDelayMinutes;
}
