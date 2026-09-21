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
/// 可以从列表选择，同时也允许手动填写
/// </summary>
public class SettingConfigCustomOptionsAttribute : Attribute
{
    public string[] Options { get; set; }

    public SettingConfigCustomOptionsAttribute(params string[] options)
    {
        Options = options;
    }
}