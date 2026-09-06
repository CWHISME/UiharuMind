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

using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 用于快捷功能设置、包括界面等相关设置
/// </summary>
public class QuickToolSetting : TConfigBase<QuickToolSetting>
{
    [SettingConfigDesc(
        "The detection interval for the clipboard, with higher frequency resulting in faster response but greater performance consumption.  \nThis setting is only effective on MacOS, with the unit being seconds, and the default value is 0.5 seconds.")]
    [SettingConfigDesc("剪切板的检测间隔，频率越高反应越快，性能消耗也越大。\n仅 MacOS 有效，单位为秒，默认 0.5 秒", LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(0.01f, 1.0f, 0.01f)]
    public float ClipboardCheckInterval { get; set; } = 0.5f;

    /// <summary>
    /// 快捷工具默认模型名(空 = 跟随顶栏当前模型)。作用于文本类快捷工具(解释/翻译/思考/询问等)。
    /// 在快捷工具设置页用下拉选择，因此不参与反射设置面板的自动渲染。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public string DefaultModelName { get; set; } = string.Empty;

    /// <summary>
    /// 快捷工具视觉类默认模型名(空 = 跟随全局视觉模型自动挑选)。作用于 OCR/识图等视觉工具。
    /// 只允许选视觉模型；在快捷工具设置页用下拉选择，不参与反射设置面板的自动渲染。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public string DefaultVisionModelName { get; set; } = string.Empty;
}