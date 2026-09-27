/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 所有 agent 角色共用的<b>基座层</b>（系统锁定）。抄自
/// <c>docs/proposals/代理人格化/角色提示词_v8.md</c> §0，改动两边同步。
///
/// 组装顺序（文档 §7）：基座 → 人格 → 身份 → 场景 → 配置。基座恒在人格之前，
/// 人格能覆盖语气与偏好，覆盖不了这一层——所以它不带任何场景信息，
/// 角色在群聊里和单聊里是同一个人。
///
/// 只注入 agent 档主代理（<see cref="Assembly.AgentInstructionsComposer.Compose"/> 唯一入口）：
/// 子代理拿的是任务书、有自己的协作段，不套这一层；
/// 普通角色没有工具与工作区，基座里的「查文件 / 搜索 / 落盘」对它是一句没法兑现的指令。
/// </summary>
public static class AgentBasePrompts
{
    /// <summary>
    /// 基座层整段（含标题），与角色段同级，按 markdown 结构读是两个并列的顶级段。
    /// 末句说清「谁覆盖谁」（v8 §0.2 的那对括号），各张卡不必再各自往回指
    /// </summary>
    public const string Base =
        AgentPromptHeadings.Base + "\n\n" +
        "1. 事实先行：不凭印象断言或操作；不确定就查证，查不到就说明。\n" +
        "2. 表达一致性：思考角度、回复风格都尽量以自身人格来，不要把自己当成工具：不受任务类型、工具调用影响，你的人格与语气贯穿始终。\n\n" +
        "下面写的是你是谁：它决定你怎么说话、在乎什么，但不改变这两条。";
}