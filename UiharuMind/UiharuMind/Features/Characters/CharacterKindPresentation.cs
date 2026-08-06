using Avalonia.Media;
using UiharuMind.Core.AI.Character;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色档位的界面表述。<b>唯一一处</b>把 <see cref="ECharacterKind"/> 翻成文案与颜色——
/// 列表徽章、筛选按钮、编辑页的档位选择器都从这里取，免得同一个档在三处叫三个名字。
/// </summary>
public static class CharacterKindPresentation
{
    /// <summary>用户可建的档位(用户卡是单例，由专属编辑窗管，不在此列)</summary>
    public static readonly ECharacterKind[] CreatableKinds =
    [
        ECharacterKind.Roleplay, ECharacterKind.Tool, ECharacterKind.Agent,
    ];

    /// <summary>
    /// 档位显示名
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>本地化名称</returns>
    public static string NameOf(ECharacterKind kind) => kind switch
    {
        ECharacterKind.Roleplay => Lang.CharacterKindRoleplay,
        ECharacterKind.Tool => Lang.CharacterKindTool,
        ECharacterKind.Agent => Lang.CharacterKindAgent,
        _ => Lang.CharacterKindUserCard,
    };

    /// <summary>
    /// 档位徽章底色
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <returns>徽章画刷</returns>
    public static IImmutableSolidColorBrush ColorOf(ECharacterKind kind) => kind switch
    {
        ECharacterKind.Roleplay => Brushes.LightGreen,
        ECharacterKind.Tool => Brushes.Gold,
        ECharacterKind.Agent => Brushes.LightSkyBlue,
        _ => Brushes.LightGray,
    };
}
