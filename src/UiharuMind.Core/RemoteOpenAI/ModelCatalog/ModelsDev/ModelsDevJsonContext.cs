using System.Text.Json.Serialization;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev 快照的序列化上下文。<b>与存档分开</b>：这份文件是 7000 多个模型的可再生缓存，
/// 用不着缩进与宽容读端，也不该让 <see cref="Core.SaveJsonContext"/> 多背一个类型。
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModelsDevCacheFile))]
[JsonSerializable(typeof(Dictionary<string, ModelsDevRawProvider>))]
internal sealed partial class ModelsDevJsonContext : JsonSerializerContext;
