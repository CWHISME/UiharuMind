using System.Reflection;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Extensions;

public static class AttributeExt
{
    public static string GetDescription(this PropertyInfo value)
    {
        return GetDescription(value.GetCustomAttributes<SettingConfigDescAttribute>()) ?? value.Name;
    }

    public static string GetDescription(this Type value)
    {
        return GetDescription(value.GetCustomAttributes<SettingConfigDescAttribute>()) ?? value.Name;
    }

    private static string? GetDescription(IEnumerable<SettingConfigDescAttribute> attributes)
    {
        SettingConfigDescAttribute? selected = null;
        foreach (var attribute in attributes)
        {
            //多语言支持，如果没有找到匹配的语言，则使用英文
            if (attribute.LanguageCode == LanguageUtils.CurCultureInfo.Name)
            {
                selected = attribute;
                break;
            }

            if (attribute.LanguageCode == LanguageUtils.EnglishUnitedStates && selected == null) selected = attribute;
        }

        if (selected == null) return null;
        return selected.Description;
    }
}