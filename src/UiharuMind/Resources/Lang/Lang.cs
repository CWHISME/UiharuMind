using System.Globalization;
using System.Resources;

namespace UiharuMind.Resources.Lang;

/// <summary>
/// Lang.resx 的资源入口。原 <c>Lang.Designer.cs</c> 由 PublicResXFileCodeGenerator 生成
/// 1074 个字符串属性，强类型化（LangKey + Loc.Text）后全部退役，仅保留
/// <see cref="ResourceManager"/> 与 <see cref="Culture"/> 两个运行时成员。
/// </summary>
public static class Lang
{
    /// <summary>
    /// Lang.resx 的资源管理器（资源嵌入在本程序集）
    /// </summary>
    public static ResourceManager ResourceManager { get; } =
        new("UiharuMind.Resources.Lang.Lang", typeof(Lang).Assembly);

    /// <summary>
    /// 当前语言（由 LocalizationManager 在 ApplyLanguage 时设置）
    /// </summary>
    public static CultureInfo? Culture { get; set; }
}
