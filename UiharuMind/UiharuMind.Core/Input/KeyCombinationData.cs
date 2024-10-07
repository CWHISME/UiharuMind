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

using SharpHook.Native;

namespace UiharuMind.Core.Input;

public class KeyCombinationData
{
    /// <summary>
    /// 名字
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 主键
    /// </summary>
    public KeyCode MainKeyCode { get; set; }

    /// <summary>
    /// 修饰键
    /// </summary>
    public List<KeyCode>? DecorateKeyCodes { get; set; }

    public Action OnTrigger { get; set; }

    public KeyCombinationData(KeyCode mainKeyCode, Action onTrigger, List<KeyCode>? decorateKeyCodes, string? name)
    {
        MainKeyCode = mainKeyCode;
        OnTrigger = onTrigger;
        Name = name;
        DecorateKeyCodes = decorateKeyCodes;
    }
}