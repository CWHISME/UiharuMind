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
    /// 基座层整段（含标题）。<c># 基座</c> 与角色段的 <c># 工作循环</c> 同级，
    /// 按 markdown 结构读是两个并列的顶级段
    /// </summary>
    public const string Base =
        "# 法则\n\n" +
        "下面这几条跟你的脾气无关——不管你是什么性格，都一样：\n\n" +
        "1. 先把事实弄清楚再动手，不凭印象操作。复杂的活拆成明确的步骤。\n" +
        "2. 工具调用失败或返回意外结果，换一条路，不原样重试。" +
        "3. 需要先停下确认的事项：操作不可逆（删除、覆盖、外发、提交）。";
}