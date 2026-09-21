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

using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 一条全局快捷键的定义。构造即定型，可安全地被钩子线程无锁读取。
/// </summary>
public sealed class KeyCombinationData
{
    /// <summary>
    /// 名字
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// 主键
    /// </summary>
    public KeyCode MainKeyCode { get; }

    /// <summary>
    /// 修饰键
    /// </summary>
    public IReadOnlyList<KeyCode>? DecorateKeyCodes { get; }

    /// <summary>
    /// 触发回调
    /// </summary>
    public Action OnTrigger { get; }

    /// 折叠后的修饰键位集，构造时算好，匹配时只剩一次整数比较
    private readonly EModifierKeys _foldedModifiers;

    public KeyCombinationData(KeyCode mainKeyCode, Action onTrigger, List<KeyCode>? decorateKeyCodes, string? name)
    {
        MainKeyCode = mainKeyCode;
        OnTrigger = onTrigger;
        Name = name;
        DecorateKeyCodes = decorateKeyCodes;
        _foldedModifiers = decorateKeyCodes.ToModifiers().Fold();
    }

    /// <summary>
    /// 判断一次按键是否命中本组合键
    /// </summary>
    /// <param name="keyCode">刚按下的主键</param>
    /// <param name="foldedModifiers">已折叠左右侧的当前修饰键位集</param>
    /// <returns>命中返回 True</returns>
    public bool Matches(KeyCode keyCode, EModifierKeys foldedModifiers)
    {
        return MainKeyCode == keyCode && _foldedModifiers == foldedModifiers;
    }

    /// <summary>
    /// 是否与另一条组合键完全同键，即两者永远不可能被区分开
    /// </summary>
    /// <param name="other">另一条组合键</param>
    /// <returns>冲突返回 True</returns>
    public bool Conflicts(KeyCombinationData other)
    {
        return MainKeyCode == other.MainKeyCode && _foldedModifiers == other._foldedModifiers;
    }
}
