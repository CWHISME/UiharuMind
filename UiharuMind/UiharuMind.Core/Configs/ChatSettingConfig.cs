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

using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class ChatSettingConfig : TConfigBase<ChatSettingConfig>
{
    
    [SettingConfigDesc("Token for model name")]
    [SettingConfigDesc("用于计算 Token 数量的模型", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions("gpt-4o", "gpt-4")]
    public string TokenForModelName { get; set; } = "gpt-4o";

    [SettingConfigDesc("alowed multi answer window")]
    [SettingConfigDesc("是否同时允许多个回答窗口存在(如果允许，每次都会开启新界面展示结果)", LanguageUtils.ChineseSimplified)]
    public bool IsAllowMultiAnswerWindow { get; set; } = false;
    
    /// <summary>
    /// 聊天是否只显示纯文本
    /// </summary>
    public bool IsChatPlainText { get; set; } = false;

    /// <summary>
    /// 思考段结束后是否自动折叠(流式进行中始终展开)
    /// </summary>
    public bool IsChatAutoCollapseThinking { get; set; } = true;

    /// <summary>
    /// 喂给模型的历史 token 预算(0 = 不限)。只影响模型输入侧的开窗,
    /// UI 渲染与磁盘历史始终全量;超预算时保最新、裁最旧。
    /// 模型侧没有上下文长度元数据可依,默认值按"128k 级远程模型留足输出/工具空间、
    /// 小上下文本地模型不至于溢出"取中。
    /// </summary>
    public int HistoryTokenBudget { get; set; } = 32_000;
}