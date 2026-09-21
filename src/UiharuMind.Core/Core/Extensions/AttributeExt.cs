using System.Reflection;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Extensions;

public static class AttributeExt
{
    /// <summary>设置项的描述（悬停说明）。没写就回退成属性名</summary>
    public static string GetDescription(this PropertyInfo value)
    {
        return Select(value.GetCustomAttributes<SettingConfigDescAttribute>()) ?? value.Name;
    }

    /// <summary>设置类的描述。没写就回退成类名</summary>
    public static string GetDescription(this Type value)
    {
        return Select(value.GetCustomAttributes<SettingConfigDescAttribute>()) ?? value.Name;
    }

    /// <summary>
    /// 设置项在界面上的显示名。没写就回退成属性名——那是英文标识符，
    /// 界面上出现它就说明这一项漏了 <see cref="SettingConfigNameAttribute"/>
    /// </summary>
    public static string GetDisplayName(this PropertyInfo value)
    {
        return Select(value.GetCustomAttributes<SettingConfigNameAttribute>()) ?? value.Name;
    }

    /// <summary>设置类在界面上的显示名。没写就回退成类名</summary>
    public static string GetDisplayName(this Type value)
    {
        return Select(value.GetCustomAttributes<SettingConfigNameAttribute>()) ?? value.Name;
    }

    //多语言支持:优先当前语言,没有则退英文,再没有则由调用方回退到标识符
    private static string? Select<T>(IEnumerable<T> attributes) where T : ILocalizedAttributeText
    {
        T? selected = default;
        foreach (T attribute in attributes)
        {
            if (attribute.LanguageCode == LanguageUtils.CurCultureInfo.Name)
            {
                selected = attribute;
                break;
            }

            if (attribute.LanguageCode == LanguageUtils.EnglishUnitedStates && selected == null) selected = attribute;
        }

        return selected?.Text;
    }
}
