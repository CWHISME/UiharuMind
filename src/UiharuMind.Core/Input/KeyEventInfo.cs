using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 一次按键事件的全部事实。
/// <para>
/// <see cref="Modifiers"/> 是**事件发生那一刻由操作系统给出的**修饰键状态，不是应用自己累加出来的。
/// 这正是修饰键卡死的解药：即便某次 release 被系统吞掉（切前台、锁屏、钩子重启都会发生），
/// 下一个事件带来的权威位集会把状态整体纠正回来，残留最多存活到下一次输入。
/// </para>
/// <para>
/// 注入事件与物理事件在这里<b>不做区分</b>：uiohook 的 simulated 位把任何进程注入的键都标成
/// 模拟（远程桌面、自动化工具、本应用回放全都一样），按它一票否决会把远程输入误伤成"自己注入"。
/// 防自触发改由注入方（如 AutoClick 回放）在注入期间挂起快捷键分发承担。
/// </para>
/// </summary>
/// <param name="KeyCode">主键键码</param>
/// <param name="Modifiers">事件时刻的权威修饰键位集</param>
public readonly record struct KeyEventInfo(KeyCode KeyCode, EModifierKeys Modifiers);
