using System.Reflection;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 嵌入资源的读取工具类
/// </summary>
public static class EmbeddedResourcesUtils
{
    /// <summary>
    /// 读取嵌入资源文件内容
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static string Read(string fileName)
    {
        var type = typeof(EmbeddedResourcesUtils);
        Assembly assembly = type.Assembly;

        var resourceName = $"{assembly.GetName().Name}.Resources.{fileName}";
        using Stream resource = assembly.GetManifestResourceStream(resourceName) ??
                                throw new FileNotFoundException($"Resource {fileName} not found.");
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 如果指定文件是 json 格式，将其格式化为具体类型返回
    /// </summary>
    /// <param name="fileName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T ReadFromJson<T>(string fileName) where T : new()
    {
        return SaveUtility.LoadFromString<T>(Read(fileName));
    }
}