/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Configs;

/// <summary>
/// Agent 工作区标量配置
/// </summary>
public class AgentSettingConfig : TConfigBase<AgentSettingConfig>
{
    /// <summary>新会话默认权限档(0 只读 / 1 自动编辑 / 2 完全自动)</summary>
    public int DefaultPermissionModeIndex { get; set; } = 1;

    /// <summary>新会话默认工作目录(空 = 不绑定)</summary>
    public string DefaultWorkspacePath { get; set; } = string.Empty;

    /// <summary>新会话默认开启 plan 模式</summary>
    public bool DefaultPlanMode { get; set; }

    /// <summary>禁用的技能名列表(SKILL.md 技能目录名)</summary>
    public List<string> DisabledSkills { get; set; } = new();
}
