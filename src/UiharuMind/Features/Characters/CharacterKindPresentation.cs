using Avalonia.Media;
using UiharuMind.Core.AI.Character;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色类别的界面表述。<b>唯一一处</b>把身份轴翻成文案与颜色——
/// 列表徽章、筛选按钮、编辑页顶栏都从这里取，免得同一类在三处叫三个名字。
///
/// 类别只有三个：普通角色 / 智能体 / 用户卡（ADR 0043）。扮演与工具人之间没有任何装配差异，
/// 它们的区别是<b>这张卡上填了什么</b>，所以合并成普通角色，存量的工具人卡也按它呈现。
/// </summary>
public static class CharacterKindPresentation
{
    /// <summary>
    /// 可建的那两类的显示名（用户卡是单例，由专属编辑窗管，不在此列）
    /// </summary>
    /// <param name="isAgent">是不是智能体</param>
    /// <returns>本地化名称</returns>
    public static string NameOf(bool isAgent) =>
        Loc.Text(isAgent ? LangKey.CharacterKindAgent : LangKey.CharacterKindChat);

    /// <summary>
    /// 角色的类别显示名
    /// </summary>
    /// <param name="character">角色</param>
    /// <returns>本地化名称</returns>
    public static string NameOf(CharacterData character) =>
        character.IsUserCard ? Loc.Text(LangKey.CharacterKindUserCard) : NameOf(character.IsAgent);

    /// <summary>
    /// 类别徽章底色
    /// </summary>
    /// <param name="character">角色</param>
    /// <returns>徽章画刷</returns>
    public static IImmutableSolidColorBrush ColorOf(CharacterData character)
    {
        if (character.IsUserCard) return Brushes.LightGray;
        return character.IsAgent ? Brushes.LightSkyBlue : Brushes.LightGreen;
    }
}
