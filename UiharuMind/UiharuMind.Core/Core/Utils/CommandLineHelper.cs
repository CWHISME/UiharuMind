using System.Reflection;
using System.Text.RegularExpressions;
using UiharuMind.Core.Core.Attributes;

namespace UiharuMind.Core.Core.Utils;

public class CommandLineHelper
{
    // 定义常见的值类型的默认值
    private static readonly int DefaultInt = default(int);
    private static readonly long DefaultLong = default(long);
    private static readonly float DefaultFloat = default(float);
    private static readonly double DefaultDouble = default(double);
    private static readonly bool DefaultBoolean = default(bool);
    private static readonly char DefaultChar = default(char);
    private static readonly DateTime DefaultDateTime = default(DateTime);

    public static string GenerateCommandLineArgs(object data)
    {
        var args = new List<string>();

        // 使用反射遍历 Data 类的属性
        foreach (var property in data.GetType().GetProperties())
        {
            var value = property.GetValue(data);

            // 如果属性值为默认值，则忽略该参数
            if (IsDefaultValue(property, value))
            {
                continue;
            }

            // 生成命令行参数
            var argName = GetArgName(property.Name);
            var argValue = GetArgValue(property, value);

            args.Add($"--{argName} {argValue}");
        }

        return string.Join(" ", args);
    }

    private static bool IsDefaultValue(PropertyInfo property, object? value)
    {
        if (value == null) return true;
        // 设置了忽略标记
        if (property.GetCustomAttribute<SettingConfigIgnoreAttribute>() != null) return true;
        Type propertyType = property.PropertyType;
        bool isValueType = propertyType.IsValueType;
        if (isValueType)
        {
            if (propertyType == typeof(int)) return value.Equals(DefaultInt);
            if (propertyType == typeof(long)) return value.Equals(DefaultLong);
            if (propertyType == typeof(float)) return value.Equals(DefaultFloat);
            if (propertyType == typeof(double)) return value.Equals(DefaultDouble);
            if (propertyType == typeof(bool)) return value.Equals(DefaultBoolean);
            if (propertyType == typeof(char)) return value.Equals(DefaultChar);
            if (propertyType == typeof(DateTime)) return value.Equals(DefaultDateTime);
        }

        if (propertyType == typeof(string)) return string.IsNullOrEmpty(value.ToString());
        return Equals(value, null);
    }

    private static string GetArgName(string propertyName)
    {
        // 将属性名称转换为命令行参数名称
        return ConvertToLowerCaseDashed(propertyName);
    }

    private static string GetArgValue(PropertyInfo property, object? value)
    {
        // 将值转换为字符串
        if (value is bool boolValue)
        {
            if (property.GetCustomAttribute<SettingConfigNoneValueAttribute>() == null)
                return boolValue ? "1" : "0";
            //没有具体值，只要有这个参数就行
            return "";
        }

        return value?.ToString() ?? "";
    }

    /// <summary>
    /// 使用正则表达式来找到大写字母，并在其前面加上短横线，然后转换为小写
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string ConvertToLowerCaseDashed(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])", "-$1").ToLower();
    }
}