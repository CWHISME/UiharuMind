/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Linq;
using System.Runtime;
using System.Threading;
using Avalonia.Threading;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Diagnostics;

/// <summary>
/// 长轮次过后把 GC 留下的<b>空洞</b>收拾成可复用状态，压住提交内存的持续增长。
///
/// 为什么需要：一轮长对话过后，实测 phys_footprint 724MB 里活对象只有 196MB，
/// 而 GC 已提交 411MB、其中 113MB 是碎片——一半的「内存占用」是运行时用过之后
/// 占着不放的空地。2026-11-24 更是一次长 agent 轮次把 committed 顶到 46GB、活对象仅 293MB，
/// 系统提交额度 87/88GB，任何程序连 192MB 都分配不出来。
///
/// ⚠️ <b>实测澄清：提交出去的region 不会因为 GC 而还给 OS。</b>
/// 用 400×8MB 数组做过对照：回收后活对象 8MB，committed 仍是 3184MB；
/// ConserveMemory=7 也一样。所以这里<b>不追求「还回去」</b>——那是 CLR 的行为，配置改不动。
/// 真正能做的是：压缩让存活对象聚拢、空 region 进入 free list 供后续分配复用，
/// 从而不再继续 commit；以及在提交量冲高时有东西 intervene，而不是无人管到系统耗尽。
///
/// 常规 GC 不会做这件事：它只在必要时回收，且默认<b>不压缩</b>——
/// 碎片留在原地等着被复用，对服务进程是对的，对一个用户盯着内存数字的桌面应用不是。
///
/// 闸门（压缩式，缺一不可）：
/// <list type="number">
/// <item><b>没有轮次在 Running。</b>压缩式 gen2 是阻塞的，几百 MB 的堆上要几十到几百毫秒，
/// 撞在流式输出中间就是一次肉眼可见的卡顿。卡在审批上的会话不算 Running——没人输出，可以回收。</item>
/// <item><b>碎片或空洞够多才值得。</b>低于阈值时这趟纯属白付一次暂停。</item>
/// <item><b>两次之间隔得够久。</b>否则一个持续繁忙的应用会被自己的回收拖垮。</item>
/// <item><b>刚切过会话就退让。</b>切换的落位排版也是几十到几百毫秒，压缩式全回收叠上去
/// 就是一次秒级冻结；页面在切完会话时调用 <see cref="NoteConversationSwitch"/> 记一笔。</item>
/// </list>
///
/// 另有一条<b>软回收</b>通道：提交量过了 <see cref="HardWatermarkBytes"/> 时，即使有轮次在跑也
/// 做一次<b>后台式非压缩</b> gen2——暂停短、不挪对象，只是让空洞进入可复用状态。
/// 这是防「一路 commit 到系统耗尽」的兜底，代价远小于压缩式撞进流式输出。
///
/// 全回收本体跑在线程池上而不是计时器回调里（计时器在 UI 线程）：阻塞式压缩的那几十到
/// 几百毫秒不该由 UI 线程发起并干等——丢给后台线程后，UI 线程只会在回收本身的
/// 各阶段被暂停，而不是从头到尾被占着。
///
/// ⚠️ 它<b>不解决</b>「活对象为什么有 196MB」。那部分归条目裁剪与历史驻留
/// （见 <c>ConversationItemWindowTrimmer</c> 与 <c>SessionResidencyPolicy</c>），
/// 这里只负责把已经死掉的那部分收拾成可复用的。两件事不要混着谈。
/// </summary>
public sealed class IdleMemoryReclaimer : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30); //巡检间隔;空闲判定本就是粗粒度的,问太密只是空转
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(3); //两次压缩式回收之间的最小间隔
    private const long FragmentationThresholdBytes = 64L * 1024 * 1024; //碎片低于这个数就不值得付那次暂停

    // 后台式回收的最小间隔。它不压缩、暂停短，允许在会话跑动时按硬水位触发
    private static readonly TimeSpan MinSoftInterval = TimeSpan.FromSeconds(45);

    // 已提交内存硬水位：过了这条线，就算有会话在跑也要做一次后台式回收。
    // 目的不是「还内存给系统」——实测 .NET GC 回收后 committed 一分不降（见类注释），
    // 而是让 gen2/LOH 的空闲 region 进入可复用状态，后续分配就地取用、不再继续 commit。
    // 2026-11-24 那次 46GB 就是长会话里一路 commit 上去的；有这条水位就不会无人干预。
    private const long HardWatermarkBytes = 2L * 1024 * 1024 * 1024;

    // 「已提交减去堆实际大小」超过这个数也算够本。光看碎片会漏掉一大类:FragmentedBytes 只数
    // 活对象之间的空隙,数不到整块空着、却没还给系统的 region。实测一次 30 分钟的会话里活对象
    // 只有 206MB,已提交却涨到 7.8GB(vmmap 里是一万七千块 256KB 的 VM_ALLOCATE),
    // 而同期碎片一直没稳定过阈值——闸门只开在碎片上就成了撞运气
    private const long UncommittedSlackThresholdBytes = 256L * 1024 * 1024;

    /// <summary>刚切完会话多久之内不回收。切换的落位排版和压缩式全回收叠在一起就是秒级冻结</summary>
    private const int BackoffAfterSwitchMs = 3000;

    /// <summary>软回收（忙时硬水位触发）的硬水位，供诊断暴露当前距离</summary>
    public const long HardWatermark = HardWatermarkBytes;

    private static long _lastConversationSwitchTicks; //Interlocked 读写:UI 线程写、回收线程读
    private static long _softReclaimCount; //后台式回收次数;诊断用
    private static long _compactReclaimCount; //压缩式回收次数;诊断用

    /// <summary>后台式（硬水位）回收累计次数</summary>
    public static long SoftReclaimCount => Interlocked.Read(ref _softReclaimCount);

    /// <summary>压缩式（空闲）回收累计次数</summary>
    public static long CompactReclaimCount => Interlocked.Read(ref _compactReclaimCount);

    private readonly DispatcherTimer _timer;
    private readonly object _reclaimLock = new();
    // 用 Environment.TickCount64 + Interlocked：Decide 在 UI 线程和线程池都会读，
    // DateTime 无 acquire/release 语义，跨线程读可能拿到过期值
    private static long _lastCompactReclaimTicks; //最后一次压缩式回收的 tick
    private static long _lastSoftReclaimTicks; //最后一次后台式回收的 tick

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

    /// <summary>
    /// 会话切换属于「正在干活」：页面壳在切完会话时调用，回收器据此把
    /// 压缩式全回收推到切换的落位排版过去之后
    /// </summary>
    public static void NoteConversationSwitch() =>
        Interlocked.Exchange(ref _lastConversationSwitchTicks, Environment.TickCount64);

    private void OnTick(object? sender, EventArgs e)
    {
        // 只判「该不该」，真正动手丢给线程池——压缩在 UI 线程上发起并干等就是一次冻结
        if (ShouldCompactReclaim()) ThreadPool.QueueUserWorkItem(_ => Reclaim(compacting: true));
        else if (ShouldSoftReclaim()) ThreadPool.QueueUserWorkItem(_ => Reclaim(compacting: false));
    }

    /// <summary>
    /// 忙时硬水位通道：只做后台式。二次判定只查同类条件，绝不翻成压缩式——
    /// 排队期间会话恰好结束也不能改性质，否则低停顿兜底会突然变成几百毫秒的阻塞压缩。
    /// </summary>
    private bool ShouldSoftReclaim()
    {
        if (!HasRunningSession()) return false; //忙通道只服务忙时；闲时有压缩通道
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        if (info.TotalCommittedBytes < HardWatermarkBytes) return false;
        // 高 committed 若几乎全是活对象，软回收既降不了占用也无 region 可复用，只是白费 CPU：
        // 空洞不到 256MB 就不值得打断推理
        if (info.TotalCommittedBytes - info.HeapSizeBytes < UncommittedSlackThresholdBytes) return false;
        if (Environment.TickCount64 - Interlocked.Read(ref _lastSoftReclaimTicks) < MinSoftInterval.TotalMilliseconds)
            return false;
        return !RecentlySwitchedConversation();
    }

    /// <summary>空闲压缩通道。等审批的会话没有流式输出，与真正空闲同等对待。</summary>
    private bool ShouldCompactReclaim()
    {
        if (HasRunningSession()) return false;
        if (Environment.TickCount64 - Interlocked.Read(ref _lastCompactReclaimTicks) < MinInterval.TotalMilliseconds)
            return false;
        if (RecentlySwitchedConversation()) return false;
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long slack = Math.Max(0, info.TotalCommittedBytes - info.HeapSizeBytes);
        return info.FragmentedBytes >= FragmentationThresholdBytes || slack >= UncommittedSlackThresholdBytes;
    }

    private void Reclaim(bool compacting)
    {
        // 二次判定只确认同类条件仍成立，不重算类型（见 ShouldSoftReclaim 注释）
        if (compacting ? !ShouldCompactReclaim() : !ShouldSoftReclaim()) return;

        lock (_reclaimLock)
        {
            long committedBefore = GC.GetGCMemoryInfo().TotalCommittedBytes;
            if (compacting)
            {
                // 压缩必须显式要:默认的 gen2 只清不挪,碎片原地留着。大对象堆另有一道开关,
                // 而长回复的正文与工具结果恰恰是大对象堆的常客
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Interlocked.Exchange(ref _lastCompactReclaimTicks, Environment.TickCount64);
                Interlocked.Increment(ref _compactReclaimCount);
            }
            else
            {
                // 后台式：暂停短，不挪对象。目的是让空闲 region 可复用，不是缩小已经 commit 的地址空间
                GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
                Interlocked.Exchange(ref _lastSoftReclaimTicks, Environment.TickCount64);
                Interlocked.Increment(ref _softReclaimCount);
            }

            long committedAfter = GC.GetGCMemoryInfo().TotalCommittedBytes;
            Log.Debug($"Idle memory reclaim ({(compacting ? "compact" : "soft")}): " +
                      $"committed {ToMb(committedBefore)} -> {ToMb(committedAfter)} MB.");
        }
    }

    private static bool HasRunningSession() =>
        SessionManager.Instance.Running.ActiveSessions()
            .Any(static pair => pair.Value == ESessionRunState.Running);

    private static bool RecentlySwitchedConversation() =>
        Environment.TickCount64 - Interlocked.Read(ref _lastConversationSwitchTicks) < BackoffAfterSwitchMs;

    private static long ToMb(long bytes) => bytes / 1048576;

    public void Dispose() => _timer.Stop();
}
