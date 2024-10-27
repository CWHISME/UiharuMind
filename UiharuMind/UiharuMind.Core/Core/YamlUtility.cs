// using UiharuMind.Core.AI.Character;
// using YamlDotNet.Serialization;
// using YamlDotNet.Serialization.NamingConventions;
//
// namespace UiharuMind.Core.Core;
//
// public class YamlUtility
// {
//     static ISerializer _serializer = new SerializerBuilder()
//         .WithNamingConvention(CamelCaseNamingConvention.Instance) // 属性名称不区分大小写
//         .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull) // 忽略 null 值
//         .Build();
//
//     static IDeserializer _deserializer = new DeserializerBuilder()
//         .WithNamingConvention(CamelCaseNamingConvention.Instance) // 属性名称不区分大小写
//         .IgnoreUnmatchedProperties() // 忽略无法匹配的属性
//         .Build();
//
//     public static string SaveToString(object target)
//     {
//         return _serializer.Serialize(target);
//     }
//
//     public static T LoadFromString<T>(string yamlString)
//     {
//         return _deserializer.Deserialize<T>(yamlString);
//     }
// }