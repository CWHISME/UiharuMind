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
/// 不在自动生成的设置界面显示
/// </summary>
public class SettingConfigIgnoreDisplayAttribute : Attribute
{
    public SettingConfigIgnoreDisplayAttribute()
    {
    }
}

/// <summary>
/// 自动取值时，忽略该属性
/// </summary>
public class SettingConfigIgnoreValueAttribute : Attribute
{
    public SettingConfigIgnoreValueAttribute()
    {
    }
}