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
/// 角色种类。这是角色的<b>唯一身份轴</b>：一个角色只落一档，界面的分类、筛选、
/// 徽章、编辑表单的面孔、以及它出现在哪一页全部直读本枚举，不再从挂载列表派生。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ECharacterKind
{
    /// <summary>
    /// 角色扮演：有人格、开场白、示例对话，可注入用户卡。无工具、无工作目录。
    /// </summary>
    Roleplay,

    /// <summary>
    /// 工具人：一段纯系统提示词干一件事(翻译、识图、解释)。无工具、无工作目录，
    /// 也没有开场白与示例对话。与 <see cref="Roleplay"/> 同样不开 harness。
    /// </summary>
    Tool,

    /// <summary>
    /// 智能体：装配工具与工作目录，走框架 harness(平台指令、技能目录、上下文压缩、
    /// 工具审批、权限档)。<b>分岔点是"要不要 agent 平台那一套"，不是工具数量</b>——
    /// 工具全关的智能体仍是智能体，它照样吃那段 harness 前言。
    /// </summary>
    Agent,

    /// <summary>
    /// 用户卡：代表「我是谁」的单例，有专属编辑窗，不进角色库、不能对话。
    /// 扮演角色靠 <see cref="CharacterData.InjectUserCard"/> 引用它(活引用，改了跟着变)。
    /// </summary>
    UserCard,
}
