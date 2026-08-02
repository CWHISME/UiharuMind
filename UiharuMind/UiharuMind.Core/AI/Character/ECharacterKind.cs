/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 角色种类。决定执行时是否装配工具与工作目录，其余差异(是否带角色扮演脚手架、
/// 是否注入用户卡)由挂载列表决定，不再靠独立旗标。
/// </summary>
public enum ECharacterKind
{
    /// <summary>
    /// 对话角色：无工具、无工作目录。系统提示 = 挂载片段 + 自身 Template + 对话模板。
    /// 既涵盖角色扮演(挂载扮演模板与用户卡)，也涵盖纯提示词的工具型角色(不挂载任何片段)。
    /// </summary>
    Roleplay,

    /// <summary>
    /// 工作区 agent：装配文件/shell/技能等工具与权限档，Template 作为人格与任务段。
    /// </summary>
    Agent,
}
