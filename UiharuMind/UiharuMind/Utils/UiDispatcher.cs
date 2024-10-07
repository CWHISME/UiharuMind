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
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Utils;

public static class UiDispatcher
{
    public static Task InvokeAsync(Action action)
    {
        return InvokeAsyncCore(() =>
        {
            action();
            return 0;
        });
    }

    public static Task<T> InvokeAsync<T>(Func<T> func)
    {
        return InvokeAsyncCore(func);
    }

    public static Task InvokeAsyncTask(Func<Task> asyncAction)
    {
        return InvokeAsyncCore(async () =>
        {
            await asyncAction();
            return 0;
        });
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

    private static async Task<T> InvokeAsyncCore<T>(Func<T> func)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(func);
        try
        {
            return await operation.GetTask().ConfigureAwait(false);
        }
        catch (Exception ex)
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