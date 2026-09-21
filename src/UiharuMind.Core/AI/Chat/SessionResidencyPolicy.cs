/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 已加载会话的<b>历史驻留策略</b>：内存里同时留几份历史，该让位的是哪几份。
///
/// 存在的理由：会话本体一经加载就留在 <see cref="SessionManager"/> 的缓存里，
/// 而历史是它身上<b>唯一一处按会话长度增长</b>的东西——实测 30 份最大的会话档
/// 落盘 48.7MB，在堆上是 164.6MB（约 3.4 倍，UTF-16 加上每条消息的对象头）。
/// 没有上限就意味着「今天打开过的每一个会话」原样压在内存里，
/// 一天下来几百 MB 全是再也不会看的历史。
///
/// 让位的是<b>历史</b>不是<b>本体</b>：本体实例必须长命——持有它的人（执行者、
/// 界面壳、后台派发）认的是那一个实例，换实例就会出现两份历史各写各的。
/// 卸掉历史则只有「持有 <c>ChatMessage</c> 实例的界面条目认不回来」这一个后果，
/// 而那正是冷会话的定义。
///
/// 做成脱离 <see cref="SessionManager"/> 的纯函数：它是<b>策略</b>而不是缓存本身，
/// 因此可以不碰文件、不碰单例直接测。
///
/// 理由见 ADR 0036（会话历史按上限驻留，本体实例永不换）。
/// </summary>
internal static class SessionResidencyPolicy
{
    /// <summary>
    /// 内存里同时保留历史的会话数上限。
    ///
    /// 取 6 是「手上正在来回切的那几个」：两个页面壳各一个当前会话，加上子会话窗口、
    /// 快捷对话与后台跑着的那一两个，还余出一点。超出的那些再打开要重读一次盘
    /// （实测约 8ms/MB），比让它们常驻划算。
    /// </summary>
    public const int MaxResidentHistories = 6;

    /// <summary>
    /// 冷却时间：最后一次访问过了这么久才允许卸。
    ///
    /// 这一条是<b>正确性</b>不是调优。后台那几条路（交回报告、定时任务、子代理派发）
    /// 都是「<c>Load</c> 出来、跨几个 await 用完再存」，中途卸载会让它们手上那份历史
    /// 成为孤儿——往里写的东西没人落盘。那些操作都在秒级之内完成，
    /// 留一分钟的余量就足以让这条竞态不可能发生，而代价只是冷会话晚一分钟让位。
    /// </summary>
    public static readonly TimeSpan MinIdleBeforeUnload = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 挑出该卸掉历史的会话
    /// </summary>
    /// <param name="lastTouched">历史仍在内存里的会话与它们的最后访问时刻</param>
    /// <param name="canUnload">这个会话此刻卸得动吗（在跑、被钉住、临时会话都卸不得）</param>
    /// <param name="now">当前时刻</param>
    /// <param name="max">驻留上限，非正值按默认处理</param>
    /// <returns>该卸掉的会话标识，最冷的排在前面</returns>
    public static IReadOnlyList<string> SelectForUnload(
        IReadOnlyDictionary<string, DateTime> lastTouched,
        Func<string, bool> canUnload,
        DateTime now,
        int max = MaxResidentHistories)
    {
        if (max <= 0) max = MaxResidentHistories;
        int excess = lastTouched.Count - max;
        if (excess <= 0) return [];

        List<string> cold = lastTouched
            .Where(x => now - x.Value >= MinIdleBeforeUnload && canUnload(x.Key))
            .OrderBy(x => x.Value)
            .Select(x => x.Key)
            .ToList();

        return cold.Count <= excess ? cold : cold.GetRange(0, excess);
    }
}
