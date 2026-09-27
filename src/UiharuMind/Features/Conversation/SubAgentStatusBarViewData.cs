/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 输入区上方的<b>子代理状态</b>：等审批、在跑、跑完了压着等交回，各占一行（见 <see cref="SubAgentStatusViewData"/>）。
/// 位置固定是它们共同的存在理由：通知会飘走、工具卡跑几十轮就滚没了，而这条随时在
/// </summary>
public sealed partial class SubAgentStatusBarViewData
{
    /// <summary>各档一行；没有的档不出现</summary>
    public ObservableCollection<SubAgentStatusViewData> Items { get; } = new();

    /// <summary>
    /// 重建状态行。整份重建而不是逐行增删：至多三行，而「哪一档有几个」是现取的快照，
    /// 比对着改反而要把同一份判据再写一遍。只在 UI 线程上调
    /// </summary>
    /// <param name="sessionId">派活的会话</param>
    /// <param name="force">内容没变也重建（换语言时文案要重算）</param>
    public void Refresh(string? sessionId, bool force = false)
    {
        List<SubAgentStatusViewData> fresh = SubAgentStatusViewData.Collect(sessionId);
        if (fresh.Count == 0 && Items.Count == 0) return;
        //一字未变就别动集合:每次运行态抖动都重建一遍会让那几行跟着闪
        if (!force && fresh.Count == Items.Count && !fresh.Where((x, i) => !x.Equals(Items[i])).Any()) return;

        Items.Clear();
        foreach (SubAgentStatusViewData status in fresh) Items.Add(status);
    }

    /// <summary>
    /// 点开这一行对应的第一个子会话。只开第一个而不是列出全部：用户要的是「马上看一眼/处理掉一个」，
    /// 处理完这一行自己会指向下一个
    /// </summary>
    /// <param name="status">被点的那一行</param>
    [RelayCommand]
    private void Open(SubAgentStatusViewData? status)
    {
        if (status is { Count: > 0 }) SubSessionWindowOpener.Open(status.SubSessionIds[0]);
    }
}
