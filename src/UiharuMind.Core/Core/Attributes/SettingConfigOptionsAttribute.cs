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

namespace UiharuMind.Core.Core.Attributes;

/// <summary>
/// 只允许从列表中选择选项
/// </summary>
public class SettingConfigOptionsAttribute : Attribute
{
    public string[] Options { get; private set; }

    public SettingConfigOptionsAttribute(params string[] options)
    {
        Options = options;
    }
}