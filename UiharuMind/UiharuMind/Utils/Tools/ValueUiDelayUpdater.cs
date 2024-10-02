using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Core.Utils.Tools;

namespace UiharuMind.Utils.Tools;

/// <summary>
/// UI线程延迟更新器
/// </summary>
/// <param name="action"></param>
/// <param name="delay"></param>
/// <typeparam name="T"></typeparam>
public class ValueUiDelayUpdater<T>(Action<T> action, int delay = 50)
    : ValueDelayUpdater<T, BackgroundScheduler>(action, delay);

// UI线程调度器的实现
public class UiScheduler : IScheduler
{
    public Task Schedule(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}