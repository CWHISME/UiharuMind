namespace UiharuMind.Core.Core.Utils.Tools;

public class ValueDelayUpdater<T, TScheduler> where TScheduler : IScheduler, new()
{
    private readonly Action<T> _action;
    private readonly int _delay;
    private bool _isProcessing;
    private T? _latestValue;
    private readonly TScheduler _scheduler = new TScheduler();

    /// <summary>
    /// 是否正在处理，如果正在处理，调用 UpdateValue 指挥更新缓存值，并等待后续实际处理
    /// </summary>
    public bool IsProcessing => _isProcessing;

    public ValueDelayUpdater(Action<T> action, int delay = 50)
    {
        _delay = delay;
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public async Task UpdateValue(T? value)
    {
        _latestValue = value;

        if (_isProcessing)
        {
            return;
        }

        _isProcessing = true;

        try
        {
            // 等待一段时间，确保高频回调不会频繁触发操作
            await Task.Delay(_delay).ConfigureAwait(false);

            // 使用调度器执行操作
            await _scheduler.Schedule(() => _action(_latestValue)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // public async Task<TReturn> UpdateValueAndReturnAsync<TReturn>(T value, Func<object, TReturn> resultSelector)
    // {
    //     TReturn result = default(TReturn);
    //
    //     await _scheduler.Schedule(async () =>
    //     {
    //         _action(value);
    //         result = resultSelector(value);
    //     });
    //
    //     return result;
    // }
}

/// <summary>
/// 后台线程延迟更新器
/// 如果是 UI 上的更新，可以使用 ValueUiDelayUpdater
/// </summary>
/// <param name="action"></param>
/// <param name="delay"></param>
/// <typeparam name="T"></typeparam>
public class ValueBackgroundDelayUpdater<T>(Action<T> action, int delay = 50)
    : ValueDelayUpdater<T, BackgroundScheduler>(action, delay);

/// <summary>
/// 后台线程字符串参数延迟更新器
/// </summary>
/// <param name="action"></param>
/// <param name="delay"></param>
public class ValueBackgroundStringDelayUpdater(Action<string> action, int delay = 50)
    : ValueBackgroundDelayUpdater<string>(action, delay);

// 调度器接口，允许不同的实现方式
public interface IScheduler
{
    Task Schedule(Action action);
}

// 非UI线程调度器的实现
public class BackgroundScheduler : IScheduler
{
    public Task Schedule(Action action)
    {
        return Task.Run(action);
    }
}