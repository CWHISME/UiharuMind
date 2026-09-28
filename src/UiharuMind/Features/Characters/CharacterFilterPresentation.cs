using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色库导航列表两条筛选轴的文案。<b>唯一一处</b>把筛选枚举翻成显示名，
/// 与 <see cref="CharacterKindPresentation"/> 同一个口径：文案只在这里出，
/// 免得同一档在菜单与别处叫两个名字。
///
/// 刻意做成方法而不是静态只读属性：<see cref="Loc"/> 读的是当前语言，
/// 静态字段会在类型首次初始化那一刻把当时的语言冻住，之后切换语言菜单就不跟着变了。
/// </summary>
public static class CharacterFilterPresentation
{
    /// <summary>
    /// 档位轴的选项，按枚举值升序，与 <see cref="ECharacterKindFilter"/> 一一对应。
    /// </summary>
    /// <returns>选项与显示名</returns>
    public static IReadOnlyList<(ECharacterKindFilter Value, string Label)> KindOptions() =>
    [
        (ECharacterKindFilter.All, Loc.Text(LangKey.All)),
        (ECharacterKindFilter.Chat, CharacterKindPresentation.NameOf(isAgent: false)),
        (ECharacterKindFilter.Agent, CharacterKindPresentation.NameOf(isAgent: true)),
    ];

    /// <summary>
    /// 来源轴的选项，按枚举值升序，与 <see cref="ECharacterOriginFilter"/> 一一对应。
    /// </summary>
    /// <returns>选项与显示名</returns>
    public static IReadOnlyList<(ECharacterOriginFilter Value, string Label)> OriginOptions() =>
    [
        (ECharacterOriginFilter.All, Loc.Text(LangKey.All)),
        (ECharacterOriginFilter.BuiltIn, Loc.Text(LangKey.CharacterOriginBuiltIn)),
        (ECharacterOriginFilter.Mine, Loc.Text(LangKey.CharacterOriginMine)),
    ];
}

/// <summary>
/// 筛选胶囊的一项：显示名 + 该档是否选中 + 点它切到哪一档。
///
/// <b>刻意不带档位类型</b>：界面只用到这三样，轴的类型留在
/// <see cref="CharacterFilterPills.Build{T}"/> 里，axaml 就不必写泛型 <c>x:DataType</c>。
/// 选中态是建的时候算好的快照，所以宿主切档后要重建这组胶囊并通知界面刷新。
/// </summary>
/// <param name="Label">显示名</param>
/// <param name="IsSelected">当前是不是这一档</param>
/// <param name="PickCommand">点它切到这一档</param>
public sealed record CharacterFilterPill(string Label, bool IsSelected, IRelayCommand PickCommand);

/// <summary>把一条筛选轴的选项铺成一组胶囊</summary>
public static class CharacterFilterPills
{
    /// <summary>
    /// 铺一组单选胶囊。
    ///
    /// 刻意不落回「下标 + Tag」的老写法：那条路把同一个值抄在常量、模型属性与
    /// axaml 的 <c>Tag</c> 三处，任一处漂移都不报错。这里选项直接来自枚举，
    /// 加一档只需在枚举里加一个成员，界面自己跟上。
    /// </summary>
    /// <typeparam name="T">筛选轴的枚举</typeparam>
    /// <param name="options">选项与显示名，来自 <see cref="CharacterFilterPresentation"/></param>
    /// <param name="current">当前选中的那一档</param>
    /// <param name="pick">选中后写回模型的回调</param>
    /// <returns>胶囊项</returns>
    public static IReadOnlyList<CharacterFilterPill> Build<T>(
        IReadOnlyList<(T Value, string Label)> options, T current, Action<T> pick) where T : struct, Enum =>
        options.Select(option => new CharacterFilterPill(option.Label,
            EqualityComparer<T>.Default.Equals(option.Value, current),
            new RelayCommand(() => pick(option.Value)))).ToList();
}
