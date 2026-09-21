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

using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Attributes;

/// <summary>
/// 放在 bool 变量上时，自动转化命令行参数时不会添加参数值(仅添加参数名字作为标记)
/// </summary>
public class SettingConfigNoneValueAttribute : Attribute
{
    public SettingConfigNoneValueAttribute()
    {
    }
}