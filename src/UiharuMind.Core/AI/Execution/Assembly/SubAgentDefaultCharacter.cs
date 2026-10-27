/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 通用子代理默认身份卡的解析：把设置页选的那张卡变成「派活时挂哪一张」。
///
/// 从前匿名子代理只有一张内置基座卡（<see cref="DefaultCharacter.AnonymousAgent"/>），
/// 人格恒为空——它就是一份任务书。用户现在可以指定一张角色卡当默认人格，
/// 于是「通用子代理」也能有稳定的说话方式与专长；不指定则一切照旧。
///
/// <b>只认通用档</b>：点名的子智能体自带人格，脑与探索档职责固定（只读整理 / 只读调研），
/// 给它们塞人设只会干扰，故这两档的解析结果恒为内置卡。
///
/// <b>公开给界面用</b>：设置页显示「当前选的是哪张卡」、以及角色被删后回退，
/// 都得用同一份判据——界面自己再写一遍「存在 + 非内部 + 非用户卡 + 未屏蔽」就会与派活侧漂移。
/// </summary>
public static class SubAgentDefaultCharacter
{
    /// <summary>
    /// 用户配置的默认卡。<b>未配置、已被删除、或不该由它担任时返回 null</b>——
    /// 调用方拿到 null 就回退内置基座卡，路径永远可用。
    ///
    /// 拒绝认领的三类：内部卡（程序点名的载体，用户没在角色库里见过它）、
    /// 用户卡（「我是谁」的单例，有自己的编辑窗）、<b>此刻不可见的屏蔽卡</b>。
    /// 屏蔽这一维走 <see cref="CharacterVisibility.PassesShield"/> 而不是硬看
    /// <see cref="CharacterData.IsShielded"/>：屏蔽管的是「内容先不给你看」，开发者解锁后它就是一张
    /// 正常角色卡——选择器列的正是「此刻可见」的那批，两边判据必须同一个，
    /// 否则会出现「在列表里选中了它，一展开却又被退回未设置」。
    /// 这些判断不信任存档：配置里可能留着用户手改的任意标识。
    /// </summary>
    /// <returns>默认角色卡；没有可用的为 null</returns>
    public static CharacterData? Resolve() => Resolve(AgentSettingConfig.Current);

    /// <inheritdoc cref="Resolve()"/>
    /// <param name="config">全局 agent 设置（显式传入以便单测，与 <see cref="SubAgentProfile.ResolveModelName"/> 同款）</param>
    public static CharacterData? Resolve(AgentSettingConfig config)
    {
        string characterId = config.DefaultSubAgentCharacterId;
        if (string.IsNullOrWhiteSpace(characterId)) return null;

        if (!CharacterManager.Instance.CharacterDataDictionary.TryGetValue(characterId, out CharacterData? card)) return null;

        return IsEligible(card, CharacterVisibility.ShowShielded) ? card : null;
    }

    /// <summary>
    /// 这张卡能不能当通用子代理的默认人格。<b>判据与角色库的候选闸门同义</b>
    /// （<see cref="CharacterVisibility.IsVisibleCandidate"/> 去掉「内部角色」那一维的反面），
    /// 选择器列的是谁、这里就认谁——两边一旦漂移，就会出现「选中了它、再展开一次又被退回未设置」。
    ///
    /// 屏蔽状态由调用方给而不是在这里现读：生产路径传 <see cref="CharacterVisibility.ShowShielded"/>，
    /// 单测直接给两个值就能覆盖「解锁 / 锁回」，不必去翻那个全局标记（翻了会和并行测试抢状态）。
    /// </summary>
    /// <param name="card">候选角色</param>
    /// <param name="shieldedVisible">此刻屏蔽角色是否可见</param>
    /// <returns>可担任返回 True</returns>
    public static bool IsEligible(CharacterData card, bool shieldedVisible) =>
        !card.IsInternal && !card.IsUserCard && (!card.IsShielded || shieldedVisible);

    /// <summary>
    /// 匿名委派该挂哪张身份卡。<b>子会话据此显示名字与头像</b>，也从它身上取人格正文，
    /// 所以这里选的必须与装配侧读到的是同一张——两处都经本类，不各自读配置。
    ///
    /// 与 <see cref="SubAgentProfile"/> 的档位一一对应：
    /// 通用档 = 用户指定的默认卡（未指定则内置基座卡）；探索档沿用「探索子代理」那张内置卡；
    /// 脑经 <c>BrainTool</c> 另建，取基座卡。
    /// </summary>
    /// <param name="type">子代理档</param>
    /// <returns>身份卡标识</returns>
    public static string IdentityIdOf(ESubAgentType type) => IdentityIdOf(type, AgentSettingConfig.Current);

    /// <inheritdoc cref="IdentityIdOf(ESubAgentType)"/>
    /// <param name="type">子代理档</param>
    /// <param name="config">全局 agent 设置</param>
    public static string IdentityIdOf(ESubAgentType type, AgentSettingConfig config)
    {
        return type switch
        {
            // 探索档恒定只读，与通用档共用一张卡会让用户分不清这次委派能不能改东西
            ESubAgentType.Explorer => nameof(DefaultCharacter.LegacyExploreAgent),
            // 脑不挂人格，刻意不跟通用档的默认卡
            ESubAgentType.Brain => nameof(DefaultCharacter.AnonymousAgent),
            _ => Resolve(config)?.CharacterId ?? nameof(DefaultCharacter.AnonymousAgent),
        };
    }
}
