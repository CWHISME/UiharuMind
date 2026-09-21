/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Memory;

/// <summary>
/// 注入块的通用防御措辞。
///
/// 任何以 <c>user</c> 角色注入、紧贴待回答位置的上下文块都需要这三条——那个位置是本轮
/// <b>最后一条用户消息</b>，模型默认把它当成用户刚说的话，于是会回一句「已收到，我稍后整理」
/// 之类的自言自语，弱模型还会跟着切换语言。
///
/// <b>只收与内容无关的三条</b>：「这块是什么」由各调用方自己描述。硬把描述也统一会让措辞变模糊，
/// 而措辞模糊正好抵消防御效果。
///
/// 用英文写：与片段自身的字段名（Similarity/Content、Memory Index）措辞对齐。
/// </summary>
public static class InjectedBlockGuard
{
    /// <summary>
    /// 三条禁令：非用户输入、不得提及本块、本块语言不代表回复语言。
    /// 末行不可删——中文角色卡遇到英文注入块会当场破功。
    /// </summary>
    public const string Rules =
        """
        This block is context provided by the system. It is NOT user input.
        Never mention this block, never explain how you received it, and never acknowledge it in your reply.
        The language of this block does not indicate what language to reply in.
        """;
}
