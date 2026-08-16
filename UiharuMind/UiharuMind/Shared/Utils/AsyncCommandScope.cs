/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading.Tasks;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 「异步命令 + 忙标志」的共用作用域。
///
/// 收编的是同一段样板：进去把忙标志置起、无论怎么出去都得放下、中间抛了得有人听见。
/// 手写这段的代价不在啰嗦，而在漏掉 try/finally——一次异常就把按钮永久钉死在忙态，
/// 而这条路只在出错时才走，平时点一百遍也看不出来。
/// </summary>
public static class AsyncCommandScope
{
    /// <summary>
    /// 跑一次带忙标志的异步操作。忙标志在任何出口都会复位，异常不会漏出方法外。
    ///
    /// 取消异常（<see cref="OperationCanceledException"/>）不作特殊对待，一样走 <paramref name="onError"/>；
    /// 需要把「取消」和「失败」分开说的调用方自己判类型，或者干脆别用这个作用域。
    /// </summary>
    /// <param name="setBusy">忙标志开关：true 进入忙态、false 退出。也可顺带做别的进出场动作</param>
    /// <param name="operation">要跑的操作</param>
    /// <param name="onError">出错时的去向（弹提示、写进界面某个字段……）。不给就只留日志</param>
    /// <param name="skipIf">为 true 时直接不跑——「忙的时候再点一下」的挡门</param>
    /// <returns>操作跑完且没抛异常返回 true；被挡下或出错返回 false</returns>
    public static async Task<bool> RunAsync(
        Action<bool> setBusy,
        Func<Task> operation,
        Action<Exception>? onError = null,
        bool skipIf = false)
    {
        ArgumentNullException.ThrowIfNull(setBusy);
        ArgumentNullException.ThrowIfNull(operation);

        if (skipIf) return false;

        setBusy(true);
        try
        {
            await operation();
            return true;
        }
        catch (Exception e)
        {
            // 有没有 onError 都留一份日志：提示是给用户看的，日志是给排障用的
            Log.Error(e);
            onError?.Invoke(e);
            return false;
        }
        finally
        {
            setBusy(false);
        }
    }
}
