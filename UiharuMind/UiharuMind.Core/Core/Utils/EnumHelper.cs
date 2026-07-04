using System.Reflection;

namespace UiharuMind.Core.Core.Utils;

public class EnumHelper
{
    public static TEnum[] GetValues<TEnum>() where TEnum : struct, Enum
    {
        // 获取枚举类型
        Type enumType = typeof(TEnum);
        // 获取所有公共静态字段（枚举成员）
        FieldInfo[] fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
        // 提取每个字段的值并转换为目标枚举类型
        TEnum[] values = new TEnum[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            values[i] = (TEnum)fields[i].GetValue(null)!;
        }
        return values;
    }
}