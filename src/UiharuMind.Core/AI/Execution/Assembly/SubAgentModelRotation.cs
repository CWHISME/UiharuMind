/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 同一档配了多个模型时怎么取：<b>依次取</b>，不是每趟都取第一个（ADR 0111）。
///
/// 起因很具体：主代理一轮里连着派出几个子代理，它们全落在设置页配的那一个模型上，
/// 而供应商的并发额度就那么几条——于是几个子代理互相排队，看起来就是「一委派就卡」。
/// 依次取用把同一批委派摊到不同模型上；代价只有顺序，没有随机：用户排的先后就是轮到它们的先后。
///
/// 游标<b>按档各一份</b>（通用 / 整理 / 探索互不干扰）且进程内共享：同一档的两次派活，
/// 无论来自哪个会话、哪条线程，都接着上一次往下取（<see cref="Interlocked"/> 保证只推进一次）。
///
/// 取用时跳过<b>钉不住的</b>（判据由调用方给，见 <c>LlmModelPinProbe.Pin</c>）：
/// 本地模型没起来、名字已不在模型库里都属此列。全都钉不住才返回 null，交回调用方回落派活者/全局模型。
///
/// 不按「谁在跑就避开谁」挑：那要看运行中的会话表，一次派活就得翻一遍全量会话，
/// 而依次取在本仓的真实用法（一轮内连派几个）里已经摊开了。真要按负载挑，改这一个类就够。
/// </summary>
internal static class SubAgentModelRotation
{
    private static readonly ConcurrentDictionary<ESubAgentType, Cursor> Cursors = new();

    /// <summary>
    /// 按配置顺序取下一条能用的模型名。
    /// </summary>
    /// <param name="type">子代理档，游标按它分开</param>
    /// <param name="candidates">配置的模型名，按用户配置的顺序</param>
    /// <param name="usable">判据：给一个模型名，钉得住就返回规范化后的名字，否则 null</param>
    /// <returns>选中的模型名；没有候选、或全都不钉不住时为 null（跟随派活者/全局）</returns>
    internal static string? Next(ESubAgentType type, IReadOnlyList<string> candidates, Func<string, string?> usable)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(usable);
        if (candidates.Count == 0) return null;

        int start = Cursors.GetOrAdd(type, static _ => new Cursor()).Advance(candidates.Count);
        for (int offset = 0; offset < candidates.Count; offset++)
        {
            string name = candidates[(start + offset) % candidates.Count];
            if (usable(name) is { } picked) return picked;
        }

        return null;
    }

    /// <summary>把游标复位到各自的第一条。给测试用：进程内状态不该跨用例泄漏</summary>
    internal static void Reset() => Cursors.Clear();

    /// <summary>一档的游标。取号用 <see cref="Interlocked"/> 递增，负溢出按无符号取模</summary>
    private sealed class Cursor
    {
        private int _ticket = -1; //从 -1 起，第一次推进正好落在 0（配置里的第一条）

        public int Advance(int count)
        {
            int ticket = Interlocked.Increment(ref _ticket);
            return (int)((uint)ticket % (uint)count);
        }
    }
}
