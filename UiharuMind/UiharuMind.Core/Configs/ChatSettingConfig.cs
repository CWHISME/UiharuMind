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

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

public class ChatSettingConfig : ConfigBase
{
    [SettingConfigDesc("Token for model name")]
    [SettingConfigDesc("用于计算 Token 数量的模型", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions("gpt-4o", "gpt-4")]
    public string TokenForModelName { get; set; } = "gpt-4o";

    [SettingConfigDesc("alowed multi answer window")]
    [SettingConfigDesc("是否同时允许多个回答窗口存在(如果允许，每次都会开启新界面展示结果)", LanguageUtils.ChineseSimplified)]
    public bool IsAllowMultiAnswerWindow { get; set; } = false;
}