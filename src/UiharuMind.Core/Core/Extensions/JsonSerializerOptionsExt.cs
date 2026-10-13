using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UiharuMind.Core.Core.Extensions;

public static class JsonSerializerOptionsExt
{
    /// <summary>
    /// 从配置里取类型信息。配合 <c>JsonSerializer.Serialize(value, typeInfo)</c> 用，替代 <c>Serialize&lt;T&gt;(value, options)</c>：
    /// 后者在 AOT 分析里一律报警，分不出配置是不是 source-gen；本仓的配置都只认 source-gen，缺类型时这里直接抛
    /// </summary>
    /// <param name="options">只认 source-gen 的序列化配置</param>
    /// <typeparam name="T">目标类型</typeparam>
    /// <returns>类型信息</returns>
    public static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
