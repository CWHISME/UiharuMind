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
/// 主代理与子代理共用（<see cref="Assembly.AgentInstructionsComposer.Compose"/> 与
/// <see cref="Assembly.SubAgentAssembly.BuildSubAgentInstructions"/> 各自注入，ADR 0066 基座共用）：
/// 子代理拿任务书也要守「不编 / 落盘 / 不可逆先问」；
/// 普通角色不装——没有工具与工作区，基座里的「查文件 / 搜索 / 落盘」对它是一句没法兑现的指令。
/// </summary>
public static class AgentBasePrompts
{
    // /// <summary>
    // /// 运行宿主：人格写不出来的系统事实。不点明的话，模型不知道「这个应用」就是它自己所在的地方，
    // /// 内置技能 uiharu-guide / uiharu-dev 的描述写的是「UiharuMind 这个应用」，它对不上（ADR 0061）。
    // /// 只给事实不指路：技能开没开因角色而异，指向一个可能不存在的技能就是在指挥不存在的工具
    // /// </summary>
    // public const string HostFact = "用户可以通过 UiharuMind 与你对话，你当前执行的任何操作也都依赖 UiharuMind 应用。在执行指令时请遵守以下规则：";

    /// <summary>
    /// 基座层整段（含标题），与角色段同级，按 markdown 结构读是两个并列的顶级段。
    /// 末句说清「谁覆盖谁」（v8 §0.2 的那对括号），各张卡不必再各自往回指
    /// </summary>
    public const string Base =
        $"""
         {AgentPromptHeadings.Base}

         你当前执行的任何操作都依赖 UiharuMind 应用。在执行指令时请遵守以下规则：
         1. 先把事实弄清楚再动手，下决定前必须先搜索项目获取足够信息，不得凭印象或猜测操作
         2. 如果工具调用失败或返回意外结果超过三次，换一条路，不原样重试
         3. **重要**：谨慎为先，不许隐瞒已知的风险或代价，碰到不可逆的操作请先做确认
         """;
}