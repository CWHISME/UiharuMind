using System;
using System.Threading.Tasks;

namespace UiharuMind.Utils;

public class UiUpdater<T>
{
    private readonly Action<T> _updateAction;
    private bool _isUpdatingUi;
    private T? _latestValue;

    public UiUpdater(Action<T> updateAction)
    {
        _updateAction = updateAction ?? throw new ArgumentNullException(nameof(updateAction));
    }

    public async Task UpdateValue(T value)
    {
        _latestValue = value;

        if (_isUpdatingUi)
        {
            return;
        }

        _isUpdatingUi = true;

        try
        {
            // 等待一段时间，确保高频回调不会频繁触发UI更新
            await Task.Delay(50);

            // 更新UI
            UiDispatcher.UnsafePost(() => _updateAction(_latestValue));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }
}