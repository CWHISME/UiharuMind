using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 一次按键事件的全部事实。
/// <para>
/// <see cref="Modifiers"/> 是**事件发生那一刻由操作系统给出的**修饰键状态，不是应用自己累加出来的。
/// 这正是修饰键卡死的解药：即便某次 release 被系统吞掉（切前台、锁屏、钩子重启都会发生），
/// 下一个事件带来的权威位集会把状态整体纠正回来，残留最多存活到下一次输入。
/// </para>
/// </summary>
/// <param name="KeyCode">主键键码</param>
/// <param name="Modifiers">事件时刻的权威修饰键位集</param>
/// <param name="IsSimulated">是否由本应用注入（<see cref="InputSimulateManager"/> 发出），用于避免自触发</param>
public readonly record struct KeyEventInfo(KeyCode KeyCode, EModifierKeys Modifiers, bool IsSimulated);
