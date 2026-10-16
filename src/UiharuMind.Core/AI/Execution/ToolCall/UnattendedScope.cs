/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 开关每批现问、通道按执行冻结：判据开关（用户中途勾上/取消）下一批就生效；
/// 通道一经取到就冻结在本执行里（计数记在通道闭包里，每批新建等于限位失效）。
/// 前台一轮、唤醒轮、离席轮各持一个，互不串扰
/// </summary>
public sealed class UnattendedScope
{
    private readonly Func<ApprovalResolver?> _current; //本批的开关：非空即授予，null 即撤销
    private ApprovalResolver? _frozen; //本执行冻结的通道

    /// <summary>构造</summary>
    /// <param name="current">
    /// 取本批的通道；null 表示本批该回落（问人/拒收由调用方定）。
    /// 每批都会调一次（撤销靠重问发现），所以必须是纯查询：只查开关、建通道，
    /// 不得有创建之外的副作用（多建的那个直接丢掉，判据没跑过）
    /// </param>
    public UnattendedScope(Func<ApprovalResolver?> current) => _current = current;

    /// <summary>本批用的通道；null 表示开关关着，调用方按自己的回落走</summary>
    /// <remarks>每批现问会多建一个待选通道，命中的才留下，其余直接丢掉（判据没跑过、无副作用）</remarks>
    /// <returns>通道；撤销时为 null</returns>
    public ApprovalResolver? ForBatch()
    {
        if (_current() is { } fresh) _frozen ??= fresh;
        else _frozen = null;
        return _frozen;
    }

    /// <summary>新执行开始：上一执行的计数不能带过来（按执行 new 的调用方用不上，仅供复用型调用方）</summary>
    public void Reset() => _frozen = null;
}
