/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// Agent 行为模式(对应框架 AgentModeProvider 的模式字符串)。
/// 新增模式仅需扩展枚举值与映射,UI 按枚举循环切换。
/// </summary>
public enum EAgentMode
{
    /// <summary>计划:先调研产出方案,不做修改</summary>
    Plan,

    /// <summary>执行:直接完成任务</summary>
    Execute,
}

/// <summary>
/// EAgentMode 辅助方法
/// </summary>
public static class AgentModeExtensions
{
    /// <summary>
    /// 映射为框架 AgentModeProvider 的模式字符串
    /// </summary>
    /// <param name="mode">模式</param>
    /// <returns>模式字符串</returns>
    public static string ToModeString(this EAgentMode mode)
    {
        return mode switch
        {
            EAgentMode.Plan => "plan",
            _ => "execute",
        };
    }

    /// <summary>
    /// 从框架模式字符串解析
    /// </summary>
    /// <param name="mode">模式字符串</param>
    /// <returns>模式枚举</returns>
    public static EAgentMode FromModeString(string? mode)
    {
        return mode == "plan" ? EAgentMode.Plan : EAgentMode.Execute;
    }

    /// <summary>
    /// 取下一个模式(循环)
    /// </summary>
    /// <param name="mode">当前模式</param>
    /// <returns>下一模式</returns>
    public static EAgentMode Next(this EAgentMode mode)
    {
        EAgentMode[] values = Enum.GetValues<EAgentMode>();
        return values[(Array.IndexOf(values, mode) + 1) % values.Length];
    }
}
