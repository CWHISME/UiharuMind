using Avalonia.Media;
using UiharuMind.Core.AI.Character;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色档位的界面表述。<b>唯一一处</b>把 <see cref="ECharacterKind"/> 翻成文案与颜色——
/// 列表徽章、筛选按钮、编辑页的档位选择器都从这里取，免得同一个档在三处叫三个名字。
/// </summary>
public static class CharacterKindPresentation
{
    /// <summary>
    /// 用户可建的档位——<b>两档</b>：普通角色 / 智能体（用户卡是单例，由专属编辑窗管，不在此列）。
    ///
    /// <b>为什么工具人不在这里了</b>（ADR 0043）：<c>Roleplay</c> 与 <c>Tool</c> 之间
    /// <b>没有任何机械差异</b>——两档都不开 harness，走同一条 <c>BuildRoleplayOptions</c>。
    /// 它们的区别（有没有人格与开场白、是不是一段纯提示词干一件事）是<b>这张卡上填了什么</b>，
    /// 不是装配管线走哪条。用身份轴表达数据差异，代价是徽章、筛选、编辑表单三处各分叉一次。
    ///
    /// ⚠️ 存量的 <c>Tool</c> 档角色一律按<b>普通角色</b>呈现（见 <see cref="NameOf"/>）。
    /// 存储层仍是四档枚举，一轴化连同迁移是 ADR 0043 的阶段 2。
    /// </summary>
    public static readonly ECharacterKind[] CreatableKinds =
    [
        ECharacterKind.Roleplay, ECharacterKind.Agent,
    ];

    /// <summary>
    /// 这个档是不是<b>普通角色</b>（扮演与工具人已合并）。筛选与排序都走它，
    /// 不要再拿 <c>Kind ==</c> 裸比较——那正是 ADR 0043 要消灭的写法。
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>普通角色返回 True</returns>
    public static bool IsPlainCharacter(ECharacterKind kind) => kind.IsChat();

    /// <summary>
    /// 档位显示名
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>本地化名称</returns>
    public static string NameOf(ECharacterKind kind) => kind switch
    {
        // 扮演与工具人合并为「角色」：两档在装配上本来就一模一样（ADR 0043）
        ECharacterKind.Roleplay or ECharacterKind.Tool => Loc.Text(LangKey.CharacterKindRoleplay),
        ECharacterKind.Agent => Loc.Text(LangKey.CharacterKindAgent),
        _ => Loc.Text(LangKey.CharacterKindUserCard),
    };

    /// <summary>
    /// 档位徽章底色
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>徽章画刷</returns>
    public static IImmutableSolidColorBrush ColorOf(ECharacterKind kind) => kind switch
    {
        ECharacterKind.Roleplay or ECharacterKind.Tool => Brushes.LightGreen,
        ECharacterKind.Agent => Brushes.LightSkyBlue,
        _ => Brushes.LightGray,
    };
}
