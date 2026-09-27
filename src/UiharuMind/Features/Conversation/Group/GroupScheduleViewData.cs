using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群的调度设置（ADR 0049 决策 1、6）：串行/并行 + 并行的停止条件。
/// 建群弹窗与右栏共用这一份——前者建时取值，后者改了即写回群壳（下一波起生效）
/// </summary>
public sealed partial class GroupScheduleViewData : ObservableObject
{
    private readonly Action<GroupScheduleViewData>? _changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSerial))]
    [NotifyPropertyChangedFor(nameof(IsParallel))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    [NotifyPropertyChangedFor(nameof(ContinueLabel))]
    private EGroupScheduleMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConservative))]
    [NotifyPropertyChangedFor(nameof(IsAggressive))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private EGroupStopPolicy _stopPolicy;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="mode">初始模式</param>
    /// <param name="stopPolicy">初始停止条件</param>
    /// <param name="changed">用户改了之后回调（写回群壳）；建群弹窗不需要为 null</param>
    public GroupScheduleViewData(EGroupScheduleMode mode, EGroupStopPolicy stopPolicy,
        Action<GroupScheduleViewData>? changed = null)
    {
        _mode = mode;
        _stopPolicy = stopPolicy;
        _changed = changed;
    }

    /// <summary>串行</summary>
    public bool IsSerial => Mode == EGroupScheduleMode.Serial;

    /// <summary>并行（停止条件只在这时显示）</summary>
    public bool IsParallel => Mode == EGroupScheduleMode.Parallel;

    /// <summary>保守档</summary>
    public bool IsConservative => StopPolicy == EGroupStopPolicy.Conservative;

    /// <summary>激进档</summary>
    public bool IsAggressive => StopPolicy == EGroupStopPolicy.Aggressive;

    /// <summary>当前选择的一句说明</summary>
    public string Hint => IsSerial
        ? Loc.Text(LangKey.GroupModeSerialHint)
        : Loc.Text(LangKey.GroupModeParallelHint) + " " + Loc.Text(IsConservative
            ? LangKey.GroupStopConservativeHint
            : LangKey.GroupStopAggressiveHint);

    /// <summary>「继续」按钮的文案：串行是再跑一圈，并行是叫醒还有新话没听的人</summary>
    public string ContinueLabel => Loc.Text(IsSerial ? LangKey.GroupContinueRound : LangKey.GroupContinueParallel);

    [RelayCommand]
    private void UseSerial() => Mode = EGroupScheduleMode.Serial;

    [RelayCommand]
    private void UseParallel() => Mode = EGroupScheduleMode.Parallel;

    [RelayCommand]
    private void UseConservative() => StopPolicy = EGroupStopPolicy.Conservative;

    [RelayCommand]
    private void UseAggressive() => StopPolicy = EGroupStopPolicy.Aggressive;

    partial void OnModeChanged(EGroupScheduleMode value) => _changed?.Invoke(this);

    partial void OnStopPolicyChanged(EGroupStopPolicy value) => _changed?.Invoke(this);
}
