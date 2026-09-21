using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 从按下/释放序列里识别「点击」（短按）。
/// 用单调时钟而非墙上时钟计时：系统对时或夏令时切换不该把一次点击算成长按。
/// </summary>
internal sealed class MouseClickDetector
{
    /// 点击阈值（毫秒），超过此时长视为长按而非点击
    private const long ClickThresholdMilliseconds = 300;

    private readonly object _stateLock = new();
    private readonly Dictionary<MouseButton, long> _pressTimes = new();

    public void BeginPress(MouseButton button)
    {
        lock (_stateLock) _pressTimes[button] = Environment.TickCount64;
    }

    /// <summary>
    /// 记录一次释放，并判定它是否构成点击
    /// </summary>
    /// <param name="button">释放的键</param>
    /// <returns>构成点击返回 True</returns>
    public bool EndPressIsClick(MouseButton button)
    {
        lock (_stateLock)
        {
            if (!_pressTimes.Remove(button, out long pressedAt)) return false;
            return Environment.TickCount64 - pressedAt <= ClickThresholdMilliseconds;
        }
    }

    public void Reset()
    {
        lock (_stateLock) _pressTimes.Clear();
    }
}
