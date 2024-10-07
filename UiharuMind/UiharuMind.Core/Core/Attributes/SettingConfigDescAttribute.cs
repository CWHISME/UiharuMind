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

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public class SettingConfigDescAttribute : Attribute
{
    public string Description { get; set; }
    public string LanguageCode { get; set; }

    public SettingConfigDescAttribute(string description, string languageCode = LanguageUtils.EnglishUnitedStates)
    {
        Description = description;
        LanguageCode = languageCode;
    }
}