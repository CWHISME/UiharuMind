/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Attributes;

/// <summary>
/// 设置项(或整个设置类)在界面上的显示名。不写则回退成属性名/类名——那是英文标识符，
/// 直接摆给用户看就是 <c>ChatPromptExecutionSettings</c> 那种东西。
/// 与 <see cref="SettingConfigDescAttribute"/> 同形状：一种语言写一条，缺当前语言时回退英文。
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public class SettingConfigNameAttribute : Attribute, ILocalizedAttributeText
{
    public string DisplayName { get; set; }
    public string LanguageCode { get; set; }

    string ILocalizedAttributeText.Text => DisplayName;

    /// <param name="displayName">显示名</param>
    /// <param name="languageCode">该文案对应的语言，默认英文</param>
    public SettingConfigNameAttribute(string displayName,
        string languageCode = LanguageUtils.EnglishUnitedStates)
    {
        DisplayName = displayName;
        LanguageCode = languageCode;
    }
}
