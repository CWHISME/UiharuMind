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
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

public class DebugSettingConfig : ConfigBase
{
    [SettingConfigDesc("log running info to console")]
    [SettingConfigDesc("运行过程中打印日志等级", LanguageUtils.ChineseSimplified)]
    [SettingConfigIgnoreValue]
    public ELogType LogTypeInfo { get; set; } = ELogType.Warning;
}