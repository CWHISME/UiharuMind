using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 按键按下状态的持有者，唯一职责是回答「这个键现在按着吗」与「这次 KeyPressed 是不是系统连发」。
/// <para>
/// 集合里的修饰键在每个按键事件上都会与操作系统给出的权威位集对账，对不上的当场剔除，
/// 因此不存在「release 丢了就永久残留」——这正是旧实现里重复触发的根源。普通键只用于连发判定，
/// 另配超时兜底；快捷键匹配从不读这里，故即便普通键残留也不可能造成误触发。
/// </para>
/// </summary>
internal sealed class KeyPressTracker
{
    /// 普通键超过此时长没等到 release，判定为漏事件并丢弃，下一次按下重新算作全新按下
    private const long StaleKeyTimeoutMilliseconds = 5000;

    private readonly object _stateLock = new();
    private readonly Dictionary<KeyCode, long> _pressedKeys = new();

    private EModifierKeys _modifiers;

    /// <summary>当前按下的修饰键位集</summary>
    public EModifierKeys Modifiers
    {
        get
        {
            lock (_stateLock) return _modifiers;
        }
    }

    /// <summary>
    /// 以权威位集刷新修饰键状态，并剔除对不上账的修饰键残留
    /// </summary>
    /// <param name="modifiers">操作系统给出的修饰键位集</param>
    public void SyncModifiers(EModifierKeys modifiers)
    {
        lock (_stateLock) SyncModifiersLocked(modifiers);
    }

    /// <summary>
    /// 记录一次按下，并判定它是否为操作系统的键盘连发。
    /// 内部先按事件里的权威位集对账再判定，故调用方无需关心两者的先后。
    /// </summary>
    /// <param name="info">按键事件</param>
    /// <returns>首次按下返回 True，连发返回 False</returns>
    public bool TryBeginPress(KeyEventInfo info)
    {
        long now = Environment.TickCount64;
        lock (_stateLock)
        {
            // 先对账：本次事件的位集是「按下之后」的状态，若某修饰键已不在其中，说明它的 release 丢了
            SyncModifiersLocked(info.Modifiers, exceptKeyCode: info.KeyCode);

            bool isFirstPress = !_pressedKeys.TryGetValue(info.KeyCode, out long pressedAt) ||
                                now - pressedAt >= StaleKeyTimeoutMilliseconds;
            _pressedKeys[info.KeyCode] = now;
            return isFirstPress;
        }
    }

    /// <summary>
    /// 记录一次释放
    /// </summary>
    /// <param name="info">按键事件</param>
    public void EndPress(KeyEventInfo info)
    {
        lock (_stateLock)
        {
            _pressedKeys.Remove(info.KeyCode);
            SyncModifiersLocked(info.Modifiers);
        }
    }

    /// <summary>
    /// 查询按下状态
    /// </summary>
    /// <param name="keyCode">键码</param>
    /// <returns>按下返回 True</returns>
    public bool IsPressed(KeyCode keyCode)
    {
        var modifier = keyCode.ToModifier();
        lock (_stateLock)
        {
            if (modifier != EModifierKeys.None) return (_modifiers & modifier) != 0;
            return _pressedKeys.TryGetValue(keyCode, out long pressedAt) &&
                   Environment.TickCount64 - pressedAt < StaleKeyTimeoutMilliseconds;
        }
    }

    /// <summary>
    /// 清空全部状态。钩子停止或重启时调用，避免跨会话的残留。
    /// </summary>
    public void Reset()
    {
        lock (_stateLock)
        {
            _pressedKeys.Clear();
            _modifiers = EModifierKeys.None;
        }
    }

    /// <param name="modifiers">权威位集</param>
    /// <param name="exceptKeyCode">正在处理的键，其自身状态由本次事件决定，不参与对账</param>
    private void SyncModifiersLocked(EModifierKeys modifiers, KeyCode exceptKeyCode = KeyCode.VcUndefined)
    {
        _modifiers = modifiers;
        if (_pressedKeys.Count == 0) return;

        List<KeyCode>? stale = null;
        foreach (var (keyCode, _) in _pressedKeys)
        {
            if (keyCode == exceptKeyCode) continue;
            var modifier = keyCode.ToModifier();
            if (modifier == EModifierKeys.None || (modifiers & modifier) != 0) continue;
            (stale ??= new List<KeyCode>()).Add(keyCode);
        }

        if (stale == null) return;
        foreach (var keyCode in stale) _pressedKeys.Remove(keyCode);
    }
}
