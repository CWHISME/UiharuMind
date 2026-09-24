/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// UI 线程调度入口。需要 await 结果时一律走这里，不要直接 await Dispatcher.UIThread.InvokeAsync：
/// 后者的续体会在 Avalonia 持有 Dispatcher 锁时同步内联执行，续体里一旦同步渲染就会与渲染线程死锁。
/// </summary>
public static class UiDispatcher
{
    public static Task InvokeAsync(Action action)
    {
        return InvokeAsync(action, DispatcherPriority.Default);
    }

    /// <summary>
    /// 按指定优先级在 UI 线程执行，常用于「让出一帧」（传空委托 + Render/Background 优先级）
    /// </summary>
    /// <param name="action">要执行的委托</param>
    /// <param name="priority">调度优先级</param>
    /// <param name="cancellationToken">执行前取消则任务以取消结束</param>
    /// <returns>委托执行完成的任务</returns>
    public static Task InvokeAsync(Action action, DispatcherPriority priority,
        CancellationToken cancellationToken = default)
    {
        return InvokeAsyncCore(() =>
        {
            action();
            return 0;
        }, priority, cancellationToken);
    }

    public static Task<T> InvokeAsync<T>(Func<T> func)
    {
        return InvokeAsyncCore(func, DispatcherPriority.Default, CancellationToken.None);
    }

    public static Task InvokeAsyncTask(Func<Task> asyncAction)
    {
        return InvokeAsyncCore(async () =>
        {
            await asyncAction();
            return 0;
        }, DispatcherPriority.Default, CancellationToken.None);
    }

    public static void SafeInvoke(Action action)
    {
        try
        {
            InvokeAsync(action).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    public static T? SafeInvoke<T>(Func<T> func)
    {
        try
        {
            return InvokeAsync(func).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            HandleException(ex);
            return default;
        }
    }

    public static void UnsafePost(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public static void FireAndForget(Action action)
    {
        InvokeAsync(action).FireAndForgetSafe();
    }

    public static void FireAndForget<T>(Func<T> func)
    {
        InvokeAsync(func).FireAndForgetSafe();
    }

    private static async Task<T> InvokeAsyncCore<T>(Func<T> func, DispatcherPriority priority,
        CancellationToken cancellationToken)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(func, priority, cancellationToken);
        try
        {
            // Avalonia 在持有 Dispatcher.InstanceLock 时完成该任务，续体若同步内联就会带锁跑下去：
            // 一旦途中同步渲染（关窗/关 Popup）去拿合成器锁，而渲染线程正持合成器锁等 Post，即死锁。
            // ForceYielding 让续体改排线程池，永不在锁内执行
            return await operation.GetTask().ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HandleException(ex);
            throw;
        }
    }

    private static void HandleException(Exception ex)
    {
        Log.Error($"UI thread invoke failed: {ex}");
    }
}

public static class TaskExtensions
{
    public static void FireAndForgetSafe(this Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                // 处理异常
                foreach (var ex in t.Exception?.Flatten().InnerExceptions ?? Enumerable.Empty<Exception>())
                {
                    Log.Error($"Task faulted: {ex}");
                }
            }
        }, TaskScheduler.Default);
    }
}