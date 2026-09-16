/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 文本文件编辑窗（TextFileWindow）的全局偏好。独立于主设置：它是独立功能，
/// 自己的开关只服务自己，不掺进 SettingConfig。改一处、存盘、所有文本窗生效。
/// </summary>
public class TextFileSettingConfig : TConfigBase<TextFileSettingConfig>
{
    [SettingConfigDesc("Text file editor: wrap long lines by default.",
        "文本文件编辑窗：默认自动换行。")]
    public bool WordWrap { get; set; } = true;

    [SettingConfigDesc("Text file editor: show line numbers by default.",
        "文本文件编辑窗：默认显示行号。")]
    public bool ShowLineNumbers { get; set; }

    [SettingConfigDesc("Text file editor: enable syntax highlighting by default.",
        "文本文件编辑窗：默认启用语法高亮。")]
    public bool SyntaxHighlight { get; set; } = true;
}