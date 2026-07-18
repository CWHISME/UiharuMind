/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Agent.Profiles;

/// <summary>
/// 子 agent 配置档案:暴露为工具的档案会注册为 Harness 的后台子 agent
/// (BackgroundAgentsProvider),由主 agent 自主调度。
/// </summary>
public class AgentProfile
{
    /// <summary>档案唯一标识</summary>
    public string ProfileId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名(注册为子 agent 时的名称)</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>档案用途描述(子 agent 的能力广告,主 agent 据此决定何时委托)</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>引用 LlmManager 模型注册表的模型名;null 表示跟随全局当前模型</summary>
    public string? ModelId { get; set; }

    /// <summary>系统提示词</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>为 true 时注册为主 agent 可调度的后台子 agent</summary>
    public bool ExposeAsTool { get; set; }
}
