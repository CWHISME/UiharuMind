/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Runtime;
using Avalonia.Threading;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Diagnostics;

/// <summary>
/// 空闲时把跑长轮次留下的那一堆<b>空洞</b>还给系统。
///
/// 为什么需要：一轮长对话过后，实测 phys_footprint 724MB 里活对象只有 196MB，
/// 而 GC 已提交 411MB、其中 113MB 是碎片——也就是说一半的「内存占用」是
/// 运行时用过之后占着不还的空地。用户看到的是 700MB，任务管理器也这么报，
/// 解释「其中一半是空的」没有意义，该做的是真的还回去。
///
/// 常规 GC 不会做这件事：它只在必要时回收，且默认<b>不压缩</b>——
/// 碎片留在原地等着被复用，对服务进程是对的，对一个用户盯着内存数字的桌面应用不是。
///
/// 三道闸，缺一不可：
/// <list type="number">
/// <item><b>没有会话在跑。</b>压缩式 gen2 是阻塞的，几百 MB 的堆上要几十到几百毫秒，
/// 撞在流式输出中间就是一次肉眼可见的卡顿。</item>
/// <item><b>碎片够多才值得。</b>低于阈值时这趟纯属白付一次暂停。</item>
/// <item><b>两次之间隔得够久。</b>否则一个持续繁忙的应用会被自己的回收拖垮。</item>
/// </list>
///
/// ⚠️ 它<b>不解决</b>「活对象为什么有 196MB」。那一半归条目裁剪与历史驻留
/// （见 <c>ConversationItemWindowTrimmer</c> 与 <c>SessionResidencyPolicy</c>），
/// 这里只负责把已经死掉却没归还的那部分还掉。两件事不要混着谈。
/// </summary>
public sealed class IdleMemoryReclaimer : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30); //巡检间隔;空闲判定本就是粗粒度的,问太密只是空转
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(3); //两次回收之间的最小间隔
    private const long FragmentationThresholdBytes = 64L * 1024 * 1024; //碎片低于这个数就不值得付那次暂停

    // 「已提交减去堆实际大小」超过这个数也算够本。光看碎片会漏掉一大类:FragmentedBytes 只数
    // 活对象之间的空隙,数不到整块空着、却没还给系统的 region。实测一次 30 分钟的会话里活对象
    // 只有 206MB,已提交却涨到 7.8GB(vmmap 里是一万七千块 256KB 的 VM_ALLOCATE),
    // 而同期碎片一直没稳定过阈值——闸门只开在碎片上就成了撞运气
    private const long UncommittedSlackThresholdBytes = 256L * 1024 * 1024;

    private readonly DispatcherTimer _timer;
    private DateTime _lastReclaimedAt = DateTime.MinValue;

    private IdleMemoryReclaimer()
    {
        _timer = new DispatcherTimer(CheckInterval, DispatcherPriority.Background, OnTick);
    }

    /// <summary>起一个巡检器并开始计时</summary>
    /// <returns>实例（应用退出时释放）</returns>
    public static IdleMemoryReclaimer Start()
    {
        IdleMemoryReclaimer reclaimer = new();
        reclaimer._timer.Start();
        return reclaimer;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!ShouldReclaim(out long fragmentedBytes, out long slackBytes)) return;

        long committedBefore = GC.GetGCMemoryInfo().TotalCommittedBytes;

        // 压缩必须显式要:默认的 gen2 只清不挪,碎片原地留着。大对象堆另有一道开关,
        // 而长回复的正文与工具结果恰恰是大对象堆的常客
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        _lastReclaimedAt = DateTime.UtcNow;
        long committedAfter = GC.GetGCMemoryInfo().TotalCommittedBytes;
        Log.Debug($"Idle memory reclaim: committed {ToMb(committedBefore)} -> {ToMb(committedAfter)} MB " +
                  $"(fragmentation {ToMb(fragmentedBytes)} MB, slack {ToMb(slackBytes)} MB).");
    }

    /// <param name="fragmentedBytes">触发时的碎片量，仅在返回 true 时有意义</param>
    /// <param name="slackBytes">触发时的「已提交未用」量，仅在返回 true 时有意义</param>
    /// <returns>此刻该不该回收</returns>
    private bool ShouldReclaim(out long fragmentedBytes, out long slackBytes)
    {
        fragmentedBytes = 0;
        slackBytes = 0;
        if (DateTime.UtcNow - _lastReclaimedAt < MinInterval) return false;

        // 任何会话在跑(含卡在审批上)都算忙:阻塞式压缩撞在流式中间就是一次可见卡顿
        if (SessionManager.Instance.Running.ActiveSessions().Count > 0) return false;

        GCMemoryInfo info = GC.GetGCMemoryInfo();
        fragmentedBytes = info.FragmentedBytes;
        slackBytes = Math.Max(0, info.TotalCommittedBytes - info.HeapSizeBytes);
        return fragmentedBytes >= FragmentationThresholdBytes || slackBytes >= UncommittedSlackThresholdBytes;
    }

    private static long ToMb(long bytes) => bytes / 1048576;

    public void Dispose() => _timer.Stop();
}
